using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

using Antlr4.Runtime;

using PWR.Compiler.Ast;

namespace PWR.Compiler.Parser;

internal readonly struct PwrParser
{
	private readonly CommonTokenStream _tokens;
	private readonly string _filename;

	public PwrParser(string code, string filename)
	{
		var reader = new StringReader(code);
		var stream = new AntlrInputStream(reader);
		var lexer = new PwrLexer(stream);
		_tokens = new CommonTokenStream(lexer);
		_tokens.Fill();
		_filename = filename;
	}

	public readonly CodeFile Parse()
	{
		var body = new List<Statement>();
		var declarations = new List<Declaration>();
		while (_tokens.LA(1) != PwrLexer.Eof) {
			switch (_tokens.LA(1)) {
				case PwrLexer.FUNCTION or PwrLexer.MODULE or PwrLexer.AT:
					declarations.Add(ParseDeclaration());
					break;
				case PwrLexer.NEWLINE:
					_tokens.Consume();
					break;
				default:
					body.Add(ParseStatement());
					break;
			}
		}
		return new CodeFile(_filename, declarations, body);
	}

	private List<T> ParseCommaList<T>(Func<T> reader, int end)
	{
		var result = new List<T>();
		Ignore(PwrLexer.NEWLINE);
		while (_tokens.LA(1) != end) {
			result.Add(reader());
			Ignore(PwrLexer.NEWLINE);
			if (_tokens.LA(1) == PwrLexer.COMMA) {
				_tokens.Consume();
				Ignore(PwrLexer.NEWLINE);
			} else {
				break;
			}
		}
		Expect(end);
		return result;
	}

	private Declaration ParseDeclaration()
	{
		var annotations = new List<Annotation>();
		while (_tokens.LA(1) == PwrLexer.AT) {
			annotations.Add(ParseAnnotation());
		}
		return _tokens.LA(1) switch {
			PwrLexer.FUNCTION => ParseFunctionDeclaration(annotations),
			PwrLexer.MODULE => ParseModuleDeclaration(annotations),
			_ => throw new UnreachableException(),
		};
	}

	private Annotation ParseAnnotation()
	{
		var pos = GetPosition(_tokens.LT(1));
		_tokens.Consume();
		var annotation = ParseIdentifier();
		if (annotation is not FunctionCallExpression fc) {
			throw new ParseError(annotation.Position, "Annotations must be written as a function call");
		}
		Expect(PwrLexer.NEWLINE);
		while (TryConsume(PwrLexer.NEWLINE)) { }
		return new Annotation(pos, fc);
	}

	private FunctionDeclaration ParseFunctionDeclaration(List<Annotation> annotations)
	{
		var pos = GetPosition(_tokens.LT(1));
		_tokens.Consume();
		var isAbstract = TryConsume(PwrLexer.ABSTRACT);
		var id = Expect(PwrLexer.IDENTIFIER);
		var name = new Identifier(GetPosition(id), id.Text);
		Expect(PwrLexer.LPAREN);
		var parameters = ParseCommaList(ParseParamDeclaration, PwrLexer.RPAREN);
		TypeReference? retType = null;
		if (TryConsume(PwrLexer.COLON)) {
			retType = ParseTypeReference();
		}
		Expect(PwrLexer.NEWLINE);
		var body = isAbstract ? [] : ParseBlock();
		var flags = isAbstract ? FunctionFlags.Abstract : FunctionFlags.None;
		return new FunctionDeclaration(pos, flags, name, parameters, retType, body) { Annotations = [..annotations] };
	}

	private ModuleDeclaration ParseModuleDeclaration(List<Annotation> annotations)
	{
		var pos = GetPosition(_tokens.LT(1));
		_tokens.Consume();
		var id = Expect(PwrLexer.IDENTIFIER);
		var name = new Identifier(GetPosition(id), id.Text);
		var body = new List<Declaration>();
		while (_tokens.LA(1) != PwrLexer.END) {
			switch (_tokens.LA(1)) {
				case PwrLexer.FUNCTION or PwrLexer.MODULE or PwrLexer.AT:
					body.Add(ParseDeclaration());
					break;
				case PwrLexer.NEWLINE:
					_tokens.Consume();
					break;
				default:
					if (_tokens.LA(1) == PwrLexer.IDENTIFIER) {
						body.Add(ParseVarDeclaration());
						Expect(PwrLexer.NEWLINE);
					} else {
						throw new ParseError(GetPosition(_tokens.LT(1)), "Unexpected module member");
					}
					break;
			}
		}
		_tokens.Consume();
		return new ModuleDeclaration(pos, name, body, null) { Annotations = [..annotations] };
	}

	private List<Statement> ParseBlock()
	{
		var result = new List<Statement>();
		Ignore(PwrLexer.NEWLINE);
		while (_tokens.LA(1) != PwrLexer.END) {
			result.Add(ParseStatement());
			Ignore(PwrLexer.NEWLINE);
		}
		Expect(PwrLexer.END);
		return result;
	}

	private List<Statement> ParseBlock(params Span<int> endpoints)
	{
		var result = new List<Statement>();
		Ignore(PwrLexer.NEWLINE);
		while (_tokens.LA(1) != PwrLexer.END && !endpoints.Contains(_tokens.LA(1))) {
			result.Add(ParseStatement());
			Ignore(PwrLexer.NEWLINE);
		}
		TryConsume(PwrLexer.END);
		return result;
	}

	private ParameterDeclaration ParseParamDeclaration()
	{
		var id = Expect(PwrLexer.IDENTIFIER);
		var name = new Identifier(GetPosition(id), id.Text);
		Expect(PwrLexer.COLON);
		var type = ParseTypeReference();
		return new ParameterDeclaration(name, type);
	}

	private TypeReference ParseTypeReference()
	{
		var id = Expect(PwrLexer.IDENTIFIER);
		TypeReference result = new SimpleTypeReference(GetPosition(id), id.Text);
		while (_tokens.LA(1) is PwrLexer.IDENTIFIER or PwrLexer.LBRACK) {
			var token = _tokens.LT(1);
			if (token.Type is PwrLexer.IDENTIFIER) {
				if (token.Text == "span") {
					result = new SpanTypeReference(result);
					_tokens.Consume();
					continue;
				}
			} else {
				_tokens.Consume();
				Expression? size = _tokens.LA(1) == PwrLexer.RBRACK ? null : ParseExpression();
				Expect(PwrLexer.RBRACK);
				result = new ArrayTypeReference(result, size);
				continue;
			}
			throw new ParseError(GetPosition(token), $"Invalid type member: '{token.Text}'");
		}
		//TODO: Add more complicated types eventually
		return result;
	}

	private Statement ParseStatement() => _tokens.LA(1) switch {
		PwrLexer.CONST or PwrLexer.LET or PwrLexer.VAR => ParseVarDeclarationStatement(),
		PwrLexer.IF => ParseIfStatement(),
		PwrLexer.FOR => ParseForStatement(),
		PwrLexer.WHILE => ParseWhileStatement(),
		PwrLexer.RETURN => ParseReturnStatement(),
		_ => new ExpressionStatement(ParseExpression())
	};

	private IfStatement ParseIfStatement()
	{
		var pos = GetPosition(_tokens.LT(1));
		_tokens.Consume();
		var cond = ParseExpression();
		Expect(PwrLexer.NEWLINE);
		var trueBlock = ParseBlock(PwrLexer.ELIF, PwrLexer.ELSE);
		List<Statement>? falseBlock = null;
		if (_tokens.LA(1) == PwrLexer.ELIF) {
			var elif = ParseIfStatement();
			falseBlock = [elif];
		} else if (_tokens.LA(1) == PwrLexer.ELSE) {
			_tokens.Consume();
			falseBlock = ParseBlock();
		}
		return new IfStatement(pos, cond, trueBlock, falseBlock);
	}

	private ForStatement ParseForStatement()
	{
		var pos = GetPosition(_tokens.LT(1));
		_tokens.Consume();
		var index = ParseVarDeclaration();
		Expect(PwrLexer.IN);
		var coll = ParseExpression();
		Expect(PwrLexer.NEWLINE);
		var body = ParseBlock();
		return new ForStatement(pos, index, coll, body);
	}

	private WhileStatement ParseWhileStatement()
	{
		var pos = GetPosition(_tokens.LT(1));
		_tokens.Consume();
		var cond = ParseExpression();
		Expect(PwrLexer.NEWLINE);
		var body = ParseBlock();
		return new WhileStatement(pos, cond, body);
	}

	private VarDeclarationStatement ParseVarDeclarationStatement()
	{
		var token = _tokens.LT(1);
		_tokens.Consume();
		var usage = token.Type switch
		{
			PwrLexer.CONST => VarUsage.Const,
			PwrLexer.LET => VarUsage.Let,
			PwrLexer.VAR => VarUsage.Var,
			_ => throw new ParseError(GetPosition(token), $"Unknown variable declaration type: '{token.Text}'")
		};
		VarDeclaration decl = ParseVarDeclaration();
		Expect(PwrLexer.ASSIGN);
		var value = ParseExpression();
		return new VarDeclarationStatement(GetPosition(token), decl, value, usage);
	}

	private VarDeclaration ParseVarDeclaration()
	{
		var name = ParseBareIdentifier();
		return new VarDeclaration(name.Position, name.Name, null);
	}

	private ReturnStatement ParseReturnStatement()
	{
		var pos = GetPosition(_tokens.LT(1));
		_tokens.Consume();
		var value = ParseExpression();
		return new ReturnStatement(pos, value);
	}

	private static readonly Dictionary<int, int> OPERATORS = new() {
		{ PwrLexer.ASSIGN, 1 },
		{ PwrLexer.MULTIPLY_EQUALS, 1 },
		{ PwrLexer.IDIV_EQUALS, 1 },
		{ PwrLexer.DIVIDE_EQUALS, 1 },
		{ PwrLexer.PLUS_EQUALS, 1 },
		{ PwrLexer.MINUS_EQUALS, 1 },
		{ PwrLexer.EQUALITY, 40 },
		{ PwrLexer.GTE, 40 },
		{ PwrLexer.LTE, 40 },
		{ PwrLexer.GT, 40 },
		{ PwrLexer.LT, 40 },
		{ PwrLexer.MULTIPLY, 20 },
		{ PwrLexer.DIVIDE, 20 },
		{ PwrLexer.IDIV, 20 },
		{ PwrLexer.MOD, 20 },
		{ PwrLexer.PLUS, 10 },
		{ PwrLexer.MINUS, 10 },
	};

	private Expression ParseExpression()
	{
		var result = ParseTerm();
		while (OPERATORS.ContainsKey(_tokens.LA(1))) {
			result = ParseBinaryOperation(result, 0);
		}
		return result;
	}

	private Expression ParseTerm() => _tokens.LA(1) switch {
		PwrLexer.CHAR_LITERAL => ParseCharLiteral(),
		PwrLexer.DOUBLE_QUOTED_STRING => ParseDqs(),
		PwrLexer.NUMBER => ParseNumber(),
		PwrLexer.IDENTIFIER => ParseIdentifier(),
		PwrLexer.LPAREN => ParseParenExpression(),
		PwrLexer.MATCH => ParseMatchExpression(),
		PwrLexer.NEW => ParseNewExpression(),
		PwrLexer.IF => ParseTernaryExpression(),
		PwrLexer.PLUS or PwrLexer.MINUS or PwrLexer.INC or PwrLexer.DEC => ParseUnaryExpression(),
		_ => throw new ParseError(GetPosition(_tokens.LT(1)), $"Unexpected expression value: '{_tokens.LT(1).Text}'")
	};

	private static readonly Dictionary<string, UnaryOperator> UNARY_OPS = new (){
		{ "+", UnaryOperator.Plus },
		{ "-", UnaryOperator.Minus },
		{ "++", UnaryOperator.Inc },
		{ "--", UnaryOperator.Dec },
	};

	private UnaryExpression ParseUnaryExpression()
	{
		var token = _tokens.LT(1);
		_tokens.Consume();
		if (!UNARY_OPS.TryGetValue(token.Text, out UnaryOperator op)) {
			throw new ParseError(GetPosition(_tokens.LT(1)), $"Unexpected unary operator: '{token.Text}'");
		}
		var expr = ParseExpression();
		return new UnaryExpression(GetPosition(token), expr, op);
	}

	private Expression ParseBinaryOperation(Expression left, int precedence)
	{
		while (true) {
			var op = _tokens.LA(1);
			if (!(OPERATORS.TryGetValue(op, out var opPrecedence) && opPrecedence >= precedence)) {
				return left;
			}
			_tokens.Consume();
			var right = ParseTerm();
			if (OPERATORS.TryGetValue(_tokens.LA(1), out var nextPrec) && opPrecedence < nextPrec) {
				right = ParseBinaryOperation(right, opPrecedence + 1);
			}
			left = MakeBinaryOperation(left, right, op);
		}
	}

	private static readonly Dictionary<int, ComparisonOperator> COMP_OPERATORS = new() {
		{ PwrLexer.EQUALITY, ComparisonOperator.Equals },
		{ PwrLexer.GTE, ComparisonOperator.GreaterThanOrEqual },
		{ PwrLexer.LTE, ComparisonOperator.LessThanOrEqual },
		{ PwrLexer.GT, ComparisonOperator.GreaterThan },
		{ PwrLexer.LT, ComparisonOperator.LessThan },
	};

	private static readonly Dictionary<int, ArithmeticOperator> MATH_OPERATORS = new() {
		{ PwrLexer.ASSIGN, ArithmeticOperator.Assign },
		{ PwrLexer.PLUS, ArithmeticOperator.Add },
		{ PwrLexer.MINUS, ArithmeticOperator.Subtract },
		{ PwrLexer.MULTIPLY, ArithmeticOperator.Multiply },
		{ PwrLexer.DIVIDE, ArithmeticOperator.Divide },
		{ PwrLexer.IDIV, ArithmeticOperator.IDivide },
		{ PwrLexer.MOD, ArithmeticOperator.Modulus },

		{ PwrLexer.PLUS_EQUALS, ArithmeticOperator.InPlaceAdd },
		{ PwrLexer.MINUS_EQUALS, ArithmeticOperator.InPlaceSub },
		{ PwrLexer.MULTIPLY_EQUALS, ArithmeticOperator.InPlaceMul},
		{ PwrLexer.DIVIDE_EQUALS, ArithmeticOperator.InPlaceDiv },
		{ PwrLexer.IDIV_EQUALS, ArithmeticOperator.InPlaceIDiv },
	};

	private static Expression MakeBinaryOperation(Expression left, Expression right, int op)
	{
		if (COMP_OPERATORS.TryGetValue(op, out var co)) {
			return new ComparisonExpression(left, right, co);
		}
		if (MATH_OPERATORS.TryGetValue(op, out var mo)) {
			return new ArithmeticExpression(left, right, mo);
		}
		throw new NotImplementedException();
	}

	private TernaryExpression ParseTernaryExpression()
	{
		var token = _tokens.LT(1);
		_tokens.Consume();
		var cond = ParseExpression();
		Expect(PwrLexer.THEN);
		var l = ParseExpression();
		Expect(PwrLexer.ELSE);
		var r = ParseExpression();
		return new TernaryExpression(GetPosition(token), cond, l, r);
	}

	private Expression ParseParenExpression()
	{
		_tokens.Consume();
		var result = ParseExpression();
		Expect(PwrLexer.RPAREN);
		return result;
	}

	private Expression ParseIdentifier()
	{
		Identifier id = ParseBareIdentifier();
		return _tokens.LA(1) switch {
			PwrLexer.DOUBLE_QUOTED_STRING or PwrLexer.IDENTIFIER or PwrLexer.NUMBER or PwrLexer.IF => ParseBareFunctionCall(id),
			PwrLexer.LPAREN => ParseFunctionCall(id),
			PwrLexer.LBRACK => ParseIndexing(id),
			_ => id
		};
	}

	private Identifier ParseBareIdentifier()
	{
		var token = _tokens.LT(1);
		_tokens.Consume();
		var name = token.Text;
		var id = new Identifier(GetPosition(token), name);
		while (TryConsume(PwrLexer.DOT)) {
			var sub = Expect(PwrLexer.IDENTIFIER);
			id = new MemberIdentifier(id, sub.Text);
		}
		return id;
	}

	private Expression ParseNewExpression()
	{
		var pos = GetPosition(_tokens.LT(1));
		_tokens.Consume();
		var type = ParseTypeReference();
		if (type is ArrayTypeReference { Size: { } size, BaseType: { } bt }) {
			return new NewArrayExpression(pos, bt, size);
		} else if (TryConsume(PwrLexer.LPAREN)) {
			var args = ParseCommaList(ParseExpression, PwrLexer.RPAREN);
			return new NewObjExpression(pos, type, args);
		} else {
			throw new NotImplementedException();
		}
	}

	private MatchExpression ParseMatchExpression()
	{
		var token = _tokens.LT(1);
		_tokens.Consume();
		var expr = ParseExpression();
		Expect(PwrLexer.LBRACE);
		var cases = ParseCommaList(ParseMatchCase, PwrLexer.RBRACE);
		return new MatchExpression(GetPosition(token), expr, cases);
	}

	private MatchCaseExpression ParseMatchCase()
	{
		var token = _tokens.LT(1);
		if (TryConsume(PwrLexer.ELSE)) {
			return new(GetPosition(token), (Expression[]?)null, ParseExpression());
		}
		var cases = ParseCommaList(ParseTerm, PwrLexer.COLON);
		if (cases.Count == 0) {
			throw new ParseError(GetPosition(token), "Match case must contain at least one case value");
		}
		return new(GetPosition(token), cases, ParseExpression());
	}

	private FunctionCallExpression ParseFunctionCall(Identifier id)
	{
		_tokens.Consume();
		var args = ParseCommaList(ParseExpression, PwrLexer.RPAREN);
		return new FunctionCallExpression(id, args);
	}

	private FunctionCallExpression ParseBareFunctionCall(Identifier id)
	{
		var args = new List<Expression>();
		while (_tokens.LA(1) is not (PwrLexer.NEWLINE or PwrLexer.Eof)) {
			args.Add(ParseExpression());
			var next = _tokens.LA(1);
			if (next == PwrLexer.COMMA) {
				_tokens.Consume();
			} else if (next is not (PwrLexer.NEWLINE or PwrLexer.Eof)) {
				throw new ParseError(GetPosition(_tokens.LT(1)), $"Unexpected function argument: '{_tokens.LT(1).Text}'");
			}
		}
		return new FunctionCallExpression(id, args);
	}

	private IndexingExpression ParseIndexing(Expression expr)
	{
		_tokens.Consume();
		var indices = ParseCommaList(ParseSlice, PwrLexer.RBRACK);
		return new IndexingExpression(expr, indices);
	}

	private Expression ParseSlice()
	{
		if (_tokens.LA(1) == PwrLexer.DOTDOT) {
			var pos = GetPosition(_tokens.LT(1));
			_tokens.Consume();
			var value = ParseExpression();
			return new SliceExpression(pos, null, value);
		}
		var start = ParseExpression();
		if (_tokens.LA(1) != PwrLexer.DOTDOT) {
			return start;
		}
		_tokens.Consume();
		if (_tokens.LA(1) is PwrLexer.COMMA or PwrLexer.RPAREN or PwrLexer.RBRACE or PwrLexer.RBRACK) {
			return new SliceExpression(start.Position, start, null);
		}
		var end = ParseExpression();
		return new SliceExpression(start.Position, start, end);
	}

	private CharLiteralExpression ParseCharLiteral()
	{
		var token = _tokens.LT(1);
		_tokens.Consume();
		var text = token.Text[1..^1];
		if (text.Contains('\\')) {
			text = Unescape(text);
		}
		return new CharLiteralExpression(GetPosition(token), text[0]);
	}

	private StringLiteralExpression ParseDqs()
	{
		var token = _tokens.LT(1);
		_tokens.Consume();
		var text = token.Text[1..^1];
		if (text.Contains('\\')) {
			text = Unescape(text);
		}
		return new StringLiteralExpression(GetPosition(token), text);
	}

	private static string Unescape(string text)
	{
		throw new NotImplementedException();
	}

	private IntegerLiteralExpression ParseNumber()
	{
		var token = _tokens.LT(1);
		_tokens.Consume();
		return new IntegerLiteralExpression(GetPosition(token), int.Parse(token.Text));
	}

	private Position GetPosition(IToken token) => new(_filename, token.Line, token.Column);

	private IToken Expect(int type)
	{
		var result = _tokens.LT(1);
		if (result.Type != type) {
			throw new ParseError(GetPosition(result), $"Unexpected token: '{result.Text}'");
		}
		_tokens.Consume();
		return result;
	}

	private bool TryConsume(int type)
	{
		if (_tokens.LA(1) == type) {
			_tokens.Consume();
			return true;
		}
		return false;
	}

	private void Ignore(int type)
	{
		while (_tokens.LA(1) == type) {
			_tokens.Consume();
		}
	}
}
