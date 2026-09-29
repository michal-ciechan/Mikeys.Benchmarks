"""Cross-language check: read an Order written by .NET, then write one back for .NET to read.

    pip install grpcio-tools
    python -m grpc_tools.protoc -I ../../proto --python_out=. ../../proto/uuid_example.proto
    dotnet run -c Release --project ../.. -- --export from-dotnet.bin
    python roundtrip.py from-dotnet.bin to-dotnet.bin
    dotnet run -c Release --project ../.. -- --import to-dotnet.bin
"""
import sys
import uuid

import uuid_example_pb2 as pb


def read_uuid(raw: bytes) -> uuid.UUID:
    # Empty/absent = not set; .NET reads that as Guid.Empty, so mirror it with the nil UUID.
    return uuid.UUID(bytes=raw) if raw else uuid.UUID(int=0)


src, dst = sys.argv[1], sys.argv[2]

order = pb.Order()
with open(src, "rb") as f:
    order.ParseFromString(f.read())

got_id = read_uuid(order.id)
got_lines = [read_uuid(b) for b in order.line_ids]
print(f"Read {src}: id={got_id} line_ids={[str(u) for u in got_lines]} quantity={order.quantity}")

assert got_id == uuid.UUID("00112233-4455-6677-8899-aabbccddeeff"), got_id
assert got_lines == [uuid.UUID("0190a6f8-4c3e-7b2a-9d1f-123456789abc"), uuid.UUID(int=0)], got_lines
assert order.quantity == 3
print("OK: Python reads the .NET Uuid bytes as the same UUIDs")

reply = pb.Order(
    id=uuid.UUID("fedcba98-7654-3210-0123-456789abcdef").bytes,  # .bytes = RFC order; NOT .bytes_le
    line_ids=[uuid.UUID("01234567-89ab-cdef-0123-456789abcdef").bytes],
    quantity=42,
)
with open(dst, "wb") as f:
    f.write(reply.SerializeToString())
print(f"Wrote {dst}: id=fedcba98-7654-3210-0123-456789abcdef line_ids=['01234567-89ab-cdef-0123-456789abcdef'] quantity=42")
