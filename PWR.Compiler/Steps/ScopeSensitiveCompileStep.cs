using System;
using System.Collections.Generic;

using PWR.Compiler.Ast;
using PWR.Compiler.Semantics;

namespace PWR.Compiler.Steps;

public abstract class ScopeSensitiveCompileStep : VisitorCompileStep
{
	protected readonly Stack<IScope> _scopes = [];

	protected List<ISemantic> Lookup(string name) => Lookup(name, SemanticType.All);

	protected List<ISemantic> Lookup(string name, SemanticType type)
	{
		var result = new List<ISemantic>();
		foreach (var scope in _scopes) {
			if (scope.Lookup(name, result, type)) {
				break;
			}
		}
		return result;
	}

	protected List<ISemantic> Scan(Func<ISemantic, bool> predicate, SemanticType type)
	{
		var result = new List<ISemantic>();
		foreach (var scope in _scopes) {
			if (scope.Scan(predicate, result, type)) {
				break;
			}
		}
		return result;
	}

	public override void VisitProject(Project node)
	{
		_scopes.Push(node);
		try {
			base.VisitProject(node);
		} finally {
			_scopes.Pop(); 
		}
	}

	public override void VisitCodeFile(CodeFile node)
	{
		_scopes.Push(node);
		try {
			base.VisitCodeFile(node);
		} finally {
			_scopes.Pop(); 
		}
	}

	public override void VisitModuleDeclaration(ModuleDeclaration node)
	{
		_scopes.Push(node);
		try {
			base.VisitModuleDeclaration(node);
		} finally {
			_scopes.Pop(); 
		}
	}

	public override void VisitStructDeclaration(StructDeclaration node)
	{
		_scopes.Push(node);
		try {
			base.VisitStructDeclaration(node);
		} finally {
			_scopes.Pop(); 
		}
	}

	public override void VisitFunctionDeclaration(FunctionDeclaration node)
	{
		_scopes.Push(node);
		try {
			base.VisitFunctionDeclaration(node);
		} finally {
			_scopes.Pop(); 
		}
	}

	public override void VisitForStatement(ForStatement node)
	{
		_scopes.Push(node);
		try {
			base.VisitForStatement(node);
		} finally {
			_scopes.Pop(); 
		}
	}

	public override void VisitBlock(Block node)
	{
		_scopes.Push(node);
		try {
			base.VisitBlock(node);
		} finally {
			_scopes.Pop(); 
		}
	}
}
