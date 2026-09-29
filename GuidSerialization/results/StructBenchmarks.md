```

BenchmarkDotNet v0.15.8, Windows 10 (10.0.19045.6466/22H2/2022Update)
Intel Core i7-6700K CPU 4.00GHz (Skylake), 1 CPU, 8 logical and 4 physical cores
.NET SDK 10.0.300
  [Host]     : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3
  Job-AGBPQC : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3

IterationCount=15  WarmupCount=3  

```
| Method                        | Categories      | N    | Mean       | Error      | StdDev     | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------------------------ |---------------- |----- |-----------:|-----------:|-----------:|------:|--------:|-------:|----------:|------------:|
| Convert_CrazyEndian           | Convert         | 1000 |   2.723 μs |  0.2601 μs |  0.2433 μs |  1.01 |    0.12 |      - |         - |          NA |
| Convert_BigEndian             | Convert         | 1000 |  23.366 μs |  1.0056 μs |  0.9406 μs |  8.64 |    0.82 |      - |         - |          NA |
| Convert_BigEndianFast         | Convert         | 1000 |   5.192 μs |  0.7702 μs |  0.7204 μs |  1.92 |    0.31 |      - |         - |          NA |
|                               |                 |      |            |            |            |       |         |        |           |             |
| De_Native                     | Deserialize     | 1000 |  57.131 μs | 10.6777 μs |  9.9879 μs |  1.03 |    0.25 | 7.5684 |   32136 B |        1.00 |
| De_StructBigEndian            | Deserialize     | 1000 |  74.378 μs | 12.4278 μs | 11.6249 μs |  1.34 |    0.31 | 7.5684 |   32136 B |        1.00 |
| De_StructCrazyEndian          | Deserialize     | 1000 |  85.948 μs |  3.9928 μs |  3.7349 μs |  1.55 |    0.27 | 7.5684 |   32136 B |        1.00 |
| De_StructCustomSerializer     | Deserialize     | 1000 |  78.879 μs |  6.1932 μs |  5.7931 μs |  1.42 |    0.26 | 7.5684 |   32136 B |        1.00 |
| De_Uuid                       | Deserialize     | 1000 |  54.333 μs |  3.9760 μs |  3.7192 μs |  0.98 |    0.18 | 7.6294 |   32136 B |        1.00 |
| De_L300Default                | Deserialize     | 1000 | 118.548 μs |  4.1080 μs |  3.6417 μs |  2.14 |    0.37 | 7.5684 |   32136 B |        1.00 |
| De_L300FixedSize              | Deserialize     | 1000 | 139.536 μs |  6.8514 μs |  6.0736 μs |  2.51 |    0.44 | 7.5684 |   32136 B |        1.00 |
|                               |                 |      |            |            |            |       |         |        |           |             |
| Ser_Native                    | Serialize       | 1000 |  86.993 μs |  3.2960 μs |  3.0831 μs |  1.00 |    0.05 | 3.7842 |   16112 B |        1.00 |
| Ser_StructBigEndian           | Serialize       | 1000 | 136.351 μs | 15.8824 μs | 14.8564 μs |  1.57 |    0.17 | 3.7842 |   16112 B |        1.00 |
| Ser_StructCrazyEndian         | Serialize       | 1000 | 141.269 μs | 13.7013 μs | 12.8162 μs |  1.63 |    0.15 | 3.7842 |   16112 B |        1.00 |
| Ser_StructCustomSerializer    | Serialize       | 1000 |  86.641 μs | 10.2653 μs |  9.6022 μs |  1.00 |    0.11 | 3.7842 |   16112 B |        1.00 |
| Ser_Uuid                      | Serialize       | 1000 |  34.791 μs |  3.3387 μs |  3.1230 μs |  0.40 |    0.04 | 3.8452 |   16112 B |        1.00 |
| Ser_L300Default               | Serialize       | 1000 |  52.421 μs |  8.1720 μs |  7.6441 μs |  0.60 |    0.09 | 3.8452 |   16112 B |        1.00 |
| Ser_L300FixedSize             | Serialize       | 1000 | 208.196 μs | 23.3396 μs | 21.8319 μs |  2.40 |    0.26 | 3.6621 |   16112 B |        1.00 |
|                               |                 |      |            |            |            |       |         |        |           |             |
| Stream_Native                 | SerializeStream | 1000 |  68.165 μs |  5.1474 μs |  4.8149 μs |  1.01 |    0.11 | 3.8452 |   16112 B |        1.00 |
| Stream_StructBigEndian        | SerializeStream | 1000 |  85.078 μs |  7.5672 μs |  7.0784 μs |  1.26 |    0.15 | 3.7842 |   16112 B |        1.00 |
| Stream_StructCrazyEndian      | SerializeStream | 1000 |  80.629 μs | 16.5990 μs | 15.5267 μs |  1.19 |    0.24 | 3.7842 |   16112 B |        1.00 |
| Stream_StructCustomSerializer | SerializeStream | 1000 |  54.098 μs |  7.4507 μs |  6.9694 μs |  0.80 |    0.12 | 3.8452 |   16112 B |        1.00 |
| Stream_Uuid                   | SerializeStream | 1000 |  30.112 μs |  4.3475 μs |  4.0666 μs |  0.44 |    0.07 | 3.8452 |   16112 B |        1.00 |
