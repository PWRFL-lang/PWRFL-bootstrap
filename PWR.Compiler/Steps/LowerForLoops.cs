using PWR.Compiler.Ast;

namespace PWR.Compiler.Steps;

public class LowerForLoops : TransformerCompileStep
{
	public override Node? VisitForStatement(ForStatement node)
	{
		var idx = node.Index;
		var coll = node.Coll;
		var body = Visit(node.Body)!;
		if (coll is FunctionCallExpression { Target: Identifier {Name: "range" } } range) {
			return BuildForRange(node.Position, idx, range, body);
		}
		throw new CompileError(coll, "Iterators are not supported yet");
	}

	private static Block BuildForRange(Position pos, VarDeclaration idx, FunctionCallExpression range, Statement[] body)
	{
		var init = new VarDeclarationStatement(idx.Position, idx, range.Args[0], VarUsage.Var);
		int step;
		if (range.Args.Length == 3) {
			if (range.Args[2] is not IntegerLiteralExpression il) {
				throw new CompileError(range.Args[2], "Range step value must be an integer literal.");
			}
			step = il.Value;
			if (step == 0) {
				throw new CompileError(il, "Range step value must not be 0.");
			}
		} else {
			step = 1;
		}
		var id = new Identifier(idx.Position, idx.Name);
		var end = step > 0
			? new AssignStatement(id, new IntegerLiteralExpression(idx.Position, step), AssignOperator.InPlaceAdd)
			: new AssignStatement(id, new IntegerLiteralExpression(idx.Position, -step), AssignOperator.InPlaceSub);
		body = [.. body, end];
		var id2 = new Identifier(idx.Position, idx.Name);
		var cond = new ComparisonExpression(id2, range.Args[1], step > 0 ? ComparisonOperator.LessThan : ComparisonOperator.GreaterThanOrEqual);
		return new Block([init, new WhileStatement(pos, cond, body)]);
	}
}
