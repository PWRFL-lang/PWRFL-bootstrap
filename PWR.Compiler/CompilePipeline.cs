using System;
using System.Diagnostics;

using LLVMSharp;
using LLVMSharp.Interop;

using PWR.Compiler.Ast;
using PWR.Compiler.Steps;
using PWR.Compiler.TypeSystem;

namespace PWR.Compiler;

public class CompilePipeline
{
	private readonly ICompileStep[] _steps;
	private readonly LLVMExecutionEngineRef _engine;
	private readonly LLVMModuleRef _module;
	private readonly CompileType _compileType;
	private readonly LLVMTargetMachineRef _machine;

	public CompilePipeline(CompileType compileType, IntPtr printf = 0)
	{
		var context = new LLVMContext();
		_module = context.Handle.CreateModuleWithName("pwr");
		_steps = [new AssignParents(), new SetupStandardLibrary(), new SimpleLowering(),
			new BindTypes(), new BindFunctions(), new LowerForLoops(),new BindExpressions(),
			new InferTypes(), new AddTypeConversions(), new Codegen(context, _module, printf)];
		Types.Populate(context);
		LLVM.InitializeX86TargetInfo();
		LLVM.InitializeX86Target();
		LLVM.InitializeX86TargetMC();
		LLVM.InitializeX86AsmPrinter();
		_compileType = compileType;
		if (compileType == CompileType.Jit) {
			LLVM.LinkInMCJIT();
			_engine = _module.CreateExecutionEngine();
		} else {
			var triple = LLVMTargetRef.DefaultTriple;
			var target = LLVMTargetRef.GetTargetFromTriple(triple);
			_machine = target.CreateTargetMachine(triple, "generic", "", 
				LLVMCodeGenOptLevel.LLVMCodeGenLevelDefault, LLVMRelocMode.LLVMRelocDefault, LLVMCodeModel.LLVMCodeModelDefault);
		}
	}

	private Project RunPipeline(Project tree)
	{
		foreach (var step in _steps) {
			tree = step.Run(tree);
		}
		var s = _module.PrintToString();
		_module.Verify(LLVMVerifierFailureAction.LLVMPrintMessageAction);
		return tree;
	}

	internal Action? Run(Project tree)
	{
		tree = RunPipeline(tree);
		return tree.EntryPoint == default ? null : _engine.GetPointerToGlobal<Action>(tree.EntryPoint);
	}

	internal void Compile(Project tree, string filename)
	{
		RunPipeline(tree);

		_machine.EmitToFile(_module, filename + ".obj", LLVMCodeGenFileType.LLVMObjectFile);
		var process = Process.Start(
			new ProcessStartInfo("lld-link", $"{filename}.obj /out:{filename}.exe /subsystem:console /defaultlib:ucrt /entry:main")
			{ RedirectStandardError = true, UseShellExecute = false })!;
		process.WaitForExit();
		if (process.ExitCode != 0) {
			var error = process.StandardError.ReadToEnd();
			throw new Exception($"llc failed: {error}");
		}
	}
}
