using System;
using System.Diagnostics;
using System.IO;

using LLVMSharp;
using LLVMSharp.Interop;

using PWR.Compiler.Ast;
using PWR.Compiler.Steps;
using PWR.Compiler.TypeSystem;

namespace PWR.Compiler;

public class CompilePipelineP3
{
	private readonly ICompileStep[] _steps;
	private readonly LLVMModuleRef _module;
	private readonly CompileOptions _options;

	public CompilePipelineP3(CompileOptions options)
	{
		var context = new LLVMContext();
		var name = Path.GetFileNameWithoutExtension(options.OutputFilename);
		_module = context.Handle.CreateModuleWithName("name");
		_steps = [new AssignParents(), new SetupStandardLibraryP3(options.Imports, options.NoStdLib, options.SearchPath),
			new SimpleLowering(), new BindTypes(), new BindMembers(), new LowerForLoops(), new BindExpressionsP3(),
			new AddTypeConversions(),
			new BuildMetadata(name), new CodegenP3(context, _module, name, options.ProjectType == ProjectType.Library)];
		Types.Populate(context);
		LLVM.InitializeX86TargetInfo();
		LLVM.InitializeX86Target();
		LLVM.InitializeX86TargetMC();
		LLVM.InitializeX86AsmPrinter();
		_options = options;
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

	internal CompileResult Run(Project tree)
	{
		var sw = Stopwatch.StartNew();
		try {
			tree = RunPipeline(tree);
		} catch (Exception ex) {
			return new ErrorCompileResult(ex.Message, sw);
		}
		return BuildResult(tree, sw);
	}

	private CompileResult BuildResult(Project tree, Stopwatch sw)
	{
		if (_options.CompileType == CompileType.Jit) {
			LLVM.LinkInMCJIT();
			var engine = _module.CreateExecutionEngine();
			return new JitCompileResult(tree.EntryPoint == default ? null : engine.GetPointerToGlobal<Action>(tree.EntryPoint), sw);
		} else {
			var triple = LLVMTargetRef.DefaultTriple;
			var target = LLVMTargetRef.GetTargetFromTriple(triple);
			var machine = target.CreateTargetMachine(triple, "generic", "",
				LLVMCodeGenOptLevel.LLVMCodeGenLevelDefault, LLVMRelocMode.LLVMRelocDefault, LLVMCodeModel.LLVMCodeModelDefault);
			var filename = _options.OutputFilename;
			var bareFilename = Path.GetFileNameWithoutExtension(filename);
			if (filename == bareFilename) {
				filename += _options.ProjectType == ProjectType.Executable ? ".exe" : ".dll";
			}

			machine.EmitToFile(_module, bareFilename + ".obj", LLVMCodeGenFileType.LLVMObjectFile);
			var args = $"{bareFilename}.obj /out:{filename}";
			if (_options.ProjectType == ProjectType.Executable) {
				args += " /subsystem:console /entry:runtimeMain";
			} else {
				args += $" /subsystem:windows /dll /implib:{bareFilename}.lib";
			}
			if (_options.NoStdLib) {
				args += " /nodefaultlib";
			} else {
				args += " /defaultlib:pwr";
			}
			args += " kernel32.lib ucrt.lib";
			var process = Process.Start(
				new ProcessStartInfo("lld-link", args)
				{ RedirectStandardError = true, UseShellExecute = false })!;
			process.WaitForExit();
			if (process.ExitCode != 0) {
				var error = process.StandardError.ReadToEnd();
				return new ErrorCompileResult($"llc failed: {error}", sw);
			}
			return new BuildCompileResult(filename, sw);
		}
	}
}
