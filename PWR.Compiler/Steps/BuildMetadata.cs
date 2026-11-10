using System;
using System.Linq;

using PWR.Compiler.Ast;
using PWR.Compiler.Metadata;
using PWR.Compiler.Semantics;

namespace PWR.Compiler.Steps;

internal class BuildMetadata(string name) : ScopeSensitiveCompileStep
{
	private readonly MetadataContext _context = new();

	// The project is walked once per phase:
	//
	//   Types: record every type. A member's signature refers to types by token, and it can name a
	//     type declared anywhere in the project, so all of them have to be known before any
	//     signature is written.
	//   TopLevelFunctions: functions that don't belong to a type. They have no owner
	//     (MetadataContext.NO_OWNER, which is -1) and the reader binary-searches the method table
	//     by owner, so they have to come first
	//   Members: everything that belongs to a type, in the order the types appear, so that the
	//     tables stay sorted by owner too.
	//
	// A function's parameters are added right after it, so they stay in order as well.
	private enum Phase
	{
		Types,
		TopLevelFunctions,
		Members
	}

	private Phase _phase;

	public override void VisitProject(Project node)
	{
		_context.AddCurrentLibrary(name);
		// Dependent libraries, in an order that doesn't depend on how they were listed.
		foreach (var import in node.Imports.DistinctBy(i => i.Name).OrderBy(i => i.Name, StringComparer.Ordinal)) {
			_context.AddLibraryReference(import.Name, import.Id);
		}
		foreach (var phase in Enum.GetValues<Phase>()) {
			_phase = phase;
			base.VisitProject(node);
		}
		(node.MetadataHeader, node.Metadata, node.BlobData, node.MetaclassFixups) = _context.Serialize();
		node.LibraryId = _context.LibraryTable[0].ID;
	}

	public override void VisitModuleDeclaration(ModuleDeclaration node)
	{
		VisitTypeDeclaration(node);
		base.VisitModuleDeclaration(node);
	}

	public override void VisitStructDeclaration(StructDeclaration node)
	{
		VisitTypeDeclaration(node);
		base.VisitStructDeclaration(node);
	}

	public override void VisitClassDeclaration(ClassDeclaration node)
	{
		VisitTypeDeclaration(node);
		base.VisitClassDeclaration(node);
	}

	private void VisitTypeDeclaration(Declaration node)
	{
		switch (_phase) {
			case Phase.Types:
				var parent = _scopes.Peek();
				_context.AddType(node.Semantic!, (parent as ISemanticNode)?.Semantic);
				break;
			case Phase.Members:
				_context.AddTypeAssociation(node.Semantic!);
				break;
		}
	}

	public override void VisitVarDeclarationStatement(VarDeclarationStatement node)
	{
		if (_phase == Phase.Members) {
			var parent = _scopes.Peek();
			if (node.Semantic!.SemanticType.HasFlag(SemanticType.Field)) {
				_context.AddField(node.Semantic, (parent as ISemanticNode)?.Semantic);
			}
		}
		base.VisitVarDeclarationStatement(node);
	}

	public override void VisitFieldDeclaration(FieldDeclaration node)
	{
		if (_phase == Phase.Members) {
			var parent = _scopes.Peek();
			_context.AddField(node.Semantic!, (parent as ISemanticNode)?.Semantic, node);
		}
		base.VisitFieldDeclaration(node);
	}

	public override void VisitFunctionDeclaration(FunctionDeclaration node)
	{
		var owner = (_scopes.Peek() as ISemanticNode)?.Semantic;
		var thisPhase = owner == null ? Phase.TopLevelFunctions : Phase.Members;
		if (_phase == thisPhase) {
			_context.AddFunction((IFunction)node.Semantic!, owner);
			base.VisitFunctionDeclaration(node);
		}
	}

	public override void VisitParameterDeclaration(ParameterDeclaration node)
	{
		var parent = _scopes.Peek();
		_context.AddParam((ParamDef)node.Semantic!, (parent as ISemanticNode)?.Semantic);
		base.VisitParameterDeclaration(node);
	}
}
