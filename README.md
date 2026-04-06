# PWRFL: Performance Weighted Runtime Focused Language

PWRFL is an attempt to build a systems programming language from scratch, incorporating concepts from modern applications programming languages while attempting to implement them in a maximally-performant manner.

## Bootstrapping

This repo documents the process of building up a compiler and runtime library for PWRFL.  Beginning with a simple compiler written in C# and a LLVM backend, it iterates through various phases of development, until it's able to build a working runtime library whose only dependencies are OS API libraries, (ie. no libc,) and then a PWRFL compiler written in PWRFL.

At the completion of each phase other than the first, (because this repo was not created before the second phase was completed,) a tag will be created, documenting the progress of the bootstrapping project at that point.  Each phase implements a certain piece of functionality, and demonstrates it is working correctly through NUnit tests.

## Phases

* Phase 1:
  * Build code in-memory and run via LLVM JIT
  * Output is printed via compiler magic
  * Demonstrate printing to stdout
  * Demonstrate defining functions, including recursive functions
  * Demonstrate basic arithmetic
  * Demonstrate simple case-matching and ternary operators
  * Demonstrate working control flow with `if`, `while`, and `for` statements
* Phase 2:
  * Build working binary executables via LLD
  * Implement FFI (foreign function interface)
  * Use FFI to implement output printing via importing `puts` from libc
  * Demonstrate working FFI by printing Hello World to the console with `puts`
* Phase 3 (in progress):
  * Build working shared libraries
  * Create a simple runtime with no dependencies on libc
  * Add self-describing metadata to shared libraries, so that PWRFL code can import from these libraries with no need for external files to describe them
  * Demonstrate a working runtime by porting existing tests from Phase 1