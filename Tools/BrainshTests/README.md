# Brainsh test suite

Differential tests for the compiler and the virtual machine in `Assets/Scripts/Brainsh`.

The suite compiles the reference interpreter from
[github.com/Facts566/Brainsh](https://github.com/Facts566/Brainsh) (`ref/bsh.cpp`)
and compares the output **byte for byte** with our implementation:

* all examples from the repository (`examples/`);
* extra probes for wrapping, arithmetic, conditions, comments, macros, includes,
  input handling and the tape border (`probes/`);
* programs both implementations must reject;
* behaviour we accept on purpose (underscores in macro names, CRLF, ...).

## Running

```bash
./run.sh
```

Needs `dotnet` (8.0 or newer) and `g++` to build the reference on the first run.

`bshtests.csproj` compiles the scripts directly from `Assets/Scripts/Brainsh`, so
there is only one copy of the language implementation.

## Safety

A Brainfuck program may loop forever - that is the whole point of the language.
Therefore:

* every reference process is killed after 10 seconds (`ProcessTimeoutMs`), the tree
  included, and the test is reported as failed;
* our own machine runs with a 50 million step limit (`StepLimit`).

The suite can never hang or keep a core busy.

## Adding a test

1. Drop a `.bsh` file into `probes/`.
2. Add `Diff("name", input)` (outputs must match) or `Fail("name")` (both must
   refuse it) in `Main()`.
3. Run `./run.sh` and check that the reference really behaves the way the test
   expects: `./ref/bsh probes/name.bsh`.