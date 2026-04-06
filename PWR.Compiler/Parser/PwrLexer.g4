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
EQUALITY : '==';
ASSIGN : '=';

ABSTRACT : 'abstract';
CONST : 'const';
ELIF : 'elif';
ELSE : 'else';
END : 'end';
FOR : 'for';
FUNCTION : 'def';
IF : 'if';
IN : 'in';
LET : 'let';
MATCH : 'match';
MODULE : 'module';
NEW : 'new';
NULL : 'null';
REF : 'ref';
RETURN : 'return';
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
