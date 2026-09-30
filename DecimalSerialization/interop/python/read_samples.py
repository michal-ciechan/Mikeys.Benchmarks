"""Cross-language check: decode every decimal format written by .NET and compare to the expected values.

    pip install grpcio-tools
    python -m grpc_tools.protoc -I ../../proto --python_out=. ../../proto/decimal_example.proto
    dotnet run -c Release --project ../.. -- --export samples.bin
    python read_samples.py samples.bin
"""
import sys
from decimal import Decimal, getcontext

import decimal_example_pb2 as pb

getcontext().prec = 40  # wider than any value here, so scaleb/addition never round

EXPECTED = [Decimal(s) for s in
            ["0", "1", "-1", "0.1", "1.10", "123.45", "-98765.4321", "0.00000001", "12345678.9", "9999999.99999999"]]


def from_bcl(m) -> Decimal:
    mantissa = (m.hi << 64) | m.lo
    scale = m.sign_scale >> 1
    sign = m.sign_scale & 1
    return Decimal((sign, tuple(int(c) for c in str(mantissa)), -scale))


def from_units_nanos(m) -> Decimal:
    return Decimal(m.units) + Decimal(m.nanos).scaleb(-9)


def from_fixed_decimal(raw: int) -> Decimal:
    return Decimal(raw).scaleb(-8)          # sint64: protobuf has already undone the zigzag


def from_decimal64(bits: int) -> Decimal:
    """IEEE 754-2008 decimal64, binary integer decimal (BID) encoding."""
    sign = bits >> 63
    if (bits >> 61) & 0b11 == 0b11:
        if (bits >> 59) & 0b11 == 0b11:
            raise ValueError("infinity or NaN")
        exponent = (bits >> 51) & 0x3FF
        coefficient = (1 << 53) | (bits & ((1 << 51) - 1))
    else:
        exponent = (bits >> 53) & 0x3FF
        coefficient = bits & ((1 << 53) - 1)
    if coefficient > 9_999_999_999_999_999:
        coefficient = 0                     # non-canonical encodings mean zero
    return Decimal((sign, tuple(int(c) for c in str(coefficient)), exponent - 398))


def from_decimal128(raw: bytes) -> Decimal:
    """IEEE 754-2008 decimal128, BID encoding, 16 bytes little-endian."""
    bits = int.from_bytes(raw, "little")
    sign = bits >> 127
    if (bits >> 125) & 0b11 == 0b11:
        if (bits >> 123) & 0b11 == 0b11:
            raise ValueError("infinity or NaN")
        return Decimal((sign, (0,), 0))     # large-coefficient form is always non-canonical: zero
    exponent = (bits >> 113) & 0x3FFF
    coefficient = bits & ((1 << 113) - 1)
    if coefficient > 10**34 - 1:
        coefficient = 0
    return Decimal((sign, tuple(int(c) for c in str(coefficient)), exponent - 6176))


def from_packed_decimal64(raw: int) -> Decimal:
    scale = raw & 31                        # low 5 bits
    mantissa = raw >> 5                     # arithmetic shift keeps the sign
    return Decimal(mantissa).scaleb(-scale)


samples = pb.Samples()
with open(sys.argv[1], "rb") as f:
    samples.ParseFromString(f.read())

decoded = {
    "native (bcl.Decimal)": [from_bcl(m) for m in samples.native],
    "text": [Decimal(s) for s in samples.text],
    "units_nanos": [from_units_nanos(m) for m in samples.units_nanos],
    "fixed_decimal": [from_fixed_decimal(r) for r in samples.fixed_decimal],
    "decimal64 (BID)": [from_decimal64(b) for b in samples.decimal64],
    "packed_decimal64": [from_packed_decimal64(r) for r in samples.packed_decimal64],
    "decimal128 (BID)": [from_decimal128(b) for b in samples.decimal128],
}

failed = False
for name, values in decoded.items():
    ok = values == EXPECTED                 # Decimal equality: 1.10 == 1.1
    same_scale = [str(v) for v in values] == [str(e) for e in EXPECTED]
    print(f"{name:22} {'OK' if ok else 'MISMATCH'}{'' if same_scale else '  (scale differs)'}  {[str(v) for v in values]}")
    failed |= not ok

sys.exit(1 if failed else 0)
