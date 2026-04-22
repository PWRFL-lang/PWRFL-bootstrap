lexer grammar PwrLexer;

AT : '@';
COLON : ':';
COMMA : ',';
LBRACE : '{';
RBRACE : '}';
LBRACK : '[';
RBRACK : ']';
LPAREN : '(';
RPAREN : ')';
DOTDOT : '..';
DOT : '.';
QUESTION : '?';

PLUS_EQUALS : '+=';
MINUS_EQUALS : '-=';
MULTIPLY_EQUALS : '*=';
IDIV_EQUALS : '//=';
DIVIDE_EQUALS : '/=';
INC : '++';
DEC : '--';

PLUS : '+';
MINUS : '-';
MULTIPLY : '*';
IDIV : '//';
DIVIDE : '/';
MOD : '%';
GTE : '>=';
LTE : '<=';
GT : '>';
LT : '<';
NOT_EQUALS : '!=';
EQUALITY : '==';
ASSIGN : '=';

ABSTRACT : 'abstract';
CAST : 'cast';
CONST : 'const';
CONSTRUCTOR : 'constructor';
ELIF : 'elif';
ELSE : 'else';
END : 'end';
EXTENDS : 'extends';
FOR : 'for';
FUNCTION : 'def';
IF : 'if';
IN : 'in';
LET : 'let';
MATCH : 'match';
MODULE : 'module';
NAMESPACE : 'namespace';
NEW : 'new';
NIL : 'nil';
REF : 'ref';
RETURN : 'return';
SELF : 'self';
STRUCT : 'struct';
THEN : 'then';
VAR : 'var';
WHILE : 'while';

CHAR_LITERAL
	:	'\''
		~['\\\r\n]
		'\''
	;

DOUBLE_QUOTED_STRING
	:	'"'
		(
			DQS_ESC
		|	~["\\\r\n] 
		)*
		'"'
	;

fragment
DQS_ESC
	:	'\\'
		(	SESC
		|	'"'
		)
	;

fragment
SESC
	:	'r' 
	|	'n' 
	|	't' 
	|	'0' 
	|	'\\'
	;

fragment
HEXDIGIT
	:	[a-fA-F0-9]
	;

IDENTIFIER
	:	ID_LETTER
		(	ID_LETTER
		|	DIGIT
		)*
	;

NUMBER
	: DIGIT+ { InputStream.LA(1) is not ('.' or 'E' or 'e') || (InputStream.LA(1) == '.' && InputStream.LA(2) == '.' ) }?
	;

fragment
ID_LETTER
	:	[_a-zA-Z]
	|	[\u0080-\uFFFE] {char.IsLetter((char)InputStream.LA(-1))}?
	;

fragment
DIGIT
	:	[0-9]
	;

HEX_INT
	:	'0x' HEXDIGIT+
		-> type(NUMBER)
	;

WS
	:	(	[ \t\f]
		)+
		-> channel(HIDDEN)
	;

NEWLINE
	:	(	'\n'
		|	'\r' '\n'?
		)
	;

COMMENT
	: ';' ~[\r\n]* -> channel(HIDDEN) ;