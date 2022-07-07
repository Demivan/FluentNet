using System.Diagnostics.CodeAnalysis;

namespace Fluent.Syntax;

public class ParserStream
{
	protected static readonly char[] SPECIAL_LINE_START_CHARS = { '}', '.', '[', '*' };

	public string content;
	public int index;
	public int peekOffset;

	public ParserStream(string content) {
		this.content = content;
		this.index = 0;
		this.peekOffset = 0;
	}
	
	private char? safeGet(int offset) {
		if (offset >= content.Length) {
			return null;
		}
		
		return content[offset];
	}

	char? charAt(int offset) {
		if (offset > this.content.Length - 1) {
			return null;
		}

		// When the cursor is at CRLF, return LF but don't move the cursor.
		// The cursor still points to the EOL position, which in this case is the
		// beginning of the compound CRLF sequence. This ensures slices of
		// [inclusive, exclusive) continue to work properly.
		if (safeGet(offset) == '\r' && safeGet(offset + 1) == '\n') {
			return '\n';
		}

		return this.content[offset];
	}

	public char? currentChar() {
		return this.charAt(this.index);
	}

	public char? currentPeek() {
		return this.charAt(this.index + this.peekOffset);
	}

	public char? next()
	{
		this.peekOffset = 0;
		// Skip over the CRLF as if it was a single character.
		if (safeGet(this.index) == '\r' && safeGet(this.index + 1) == '\n') {
			this.index++;
		}
		this.index++;
		if (index > this.content.Length - 1) {
			return null;
		}

		return this.content[this.index];
	}

	public char? peek()
	{
		var currentPeekOffset = this.index + this.peekOffset;
		if (currentPeekOffset > this.content.Length - 1) {
			return null;
		}

		// Skip over the CRLF as if it was a single character.
		if (safeGet(currentPeekOffset) == '\r' && safeGet(currentPeekOffset + 1) == '\n') {
			this.peekOffset++;
		}
		this.peekOffset++;

		var offset = this.index + this.peekOffset;
		if (offset > this.content.Length - 1) {
			return null;
		}

		return this.content[this.index + this.peekOffset];
	}

	public void resetPeek(int offset = 0) {
		this.peekOffset = offset;
	}

	public void skipToPeek() {
		this.index += this.peekOffset;
		this.peekOffset = 0;
	}
}

internal class ParseError : Exception
{
	public string Code { get; }

	public object[] Arguments { get; }

	public ParseError(string code, params object[] arguments)
	{
		Code = code;
		Arguments = arguments;
	}
}

public class FluentParserStream : ParserStream
{
	public FluentParserStream(string content)
		: base(content)
	{
	}

	public string peekBlankInline() {
		var start = this.index + this.peekOffset;
		while (this.currentPeek() == ' ') {
			this.peek();
		}
		return this.content[start..(this.index + this.peekOffset)];
	}

	public string skipBlankInline() {
		var blank = this.peekBlankInline();
		this.skipToPeek();
		return blank;
	}

	public string peekBlankBlock() {
		var blank = "";
		while (true) {
			var lineStart = this.peekOffset;
			this.peekBlankInline();
			if (this.currentPeek() == Constants.EOL) {
				blank += Constants.EOL;
				this.peek();
				continue;
			}
			if (this.currentPeek() is null) {
				// Treat the blank line at EOF as a blank block.
				return blank;
			}
			// Any other char; reset to column 1 on this line.
			this.resetPeek(lineStart);
			return blank;
		}
	}

	public string skipBlankBlock() {
		var blank = this.peekBlankBlock();
		this.skipToPeek();
		return blank;
	}

	public void peekBlank() {
		while (this.currentPeek() == ' ' || this.currentPeek() == Constants.EOL) {
			this.peek();
		}
	}

	public void skipBlank() {
		this.peekBlank();
		this.skipToPeek();
	}

	public void expectChar (char ch) {
		if (this.currentChar() == ch) {
			this.next();
			return;
		}

		throw new ParseError("E0003", ch.ToString());
	}

	public void expectLineEnd() {
		if (this.currentChar() is null) {
			// EOF is a valid line end in Fluent.
			return;
		}

		if (this.currentChar() == Constants.EOL) {
			this.next();
			return;
		}

		// Unicode Character 'SYMBOL FOR NEWLINE' (U+2424)
		throw new ParseError("E0003", "\u2424");
	}

	public char? takeChar(Func<char, bool> f) {
		var ch = this.currentChar();
		if (ch == null) {
			return null;
		}
		if (f(ch.Value)) {
			this.next();
			return ch;
		}
		return null;
	}

	bool isCharIdStart([NotNullWhen(true)] char? ch) {
		if (ch == null) {
			return false;
		}

		return (ch >= 97 && ch <= 122) || (ch >= 65 && ch <= 90);
	}

	public bool isIdentifierStart() {
		return this.isCharIdStart(this.currentPeek());
	}

	public bool isNumberStart() {
		var ch = this.currentChar() == '-'
			? this.peek()
			: this.currentChar();

		if (ch is null) {
			this.resetPeek();
			return false;
		}

		var isDigit = IsDigit(ch.Value);
		this.resetPeek();
		return isDigit;
	}

	public bool isCharPatternContinuation (char? ch) {
		if (ch == null) {
			return false;
		}

		return !SPECIAL_LINE_START_CHARS.Contains(ch.Value);
	}

	public bool isValueStart() {
		// Inline Patterns may start with any char.
		var ch = this.currentPeek();
		return ch != Constants.EOL && ch != null;
	}

	public bool isValueContinuation() {
		var column1 = this.peekOffset;
		this.peekBlankInline();

		if (this.currentPeek() == '{') {
			this.resetPeek(column1);
			return true;
		}

		if (this.peekOffset - column1 == 0) {
			return false;
		}

		if (this.isCharPatternContinuation(this.currentPeek())) {
			this.resetPeek(column1);
			return true;
		}

		return false;
	}

	// -1 - any
	//  0 - comment
	//  1 - group comment
	//  2 - resource comment
	public bool isNextLineComment(int level) {
		if (this.currentChar() != Constants.EOL) {
			return false;
		}

		var i = 0;

		while (i <= level || (level == -1 && i < 3)) {
			if (this.peek() != '#') {
				if (i <= level && level != -1) {
					this.resetPeek();
					return false;
				}
				break;
			}
			i++;
		}

		// The first char after #, ## or ###.
		var ch = this.peek();
		if (ch is ' ' or Constants.EOL) {
			this.resetPeek();
			return true;
		}

		this.resetPeek();
		return false;
	}

	public bool isVariantStart() {
		var currentPeekOffset = this.peekOffset;
		if (this.currentPeek() == '*') {
			this.peek();
		}
		if (this.currentPeek() == '[') {
			this.resetPeek(currentPeekOffset);
			return true;
		}
		this.resetPeek(currentPeekOffset);
		return false;
	}

	public void skipToNextEntryStart(int junkStart) {
		if (this.index > this.content.Length - 1)
			return;

		var lastNewline = this.content.LastIndexOf(Constants.EOL, this.index);
		if (junkStart < lastNewline) {
			// Last seen newline is _after_ the junk start. It's safe to rewind
			// without the risk of resuming at the same broken entry.
			this.index = lastNewline;
		}
		while (this.currentChar() is not null) {
			// We're only interested in beginnings of line.
			if (this.currentChar() != Constants.EOL) {
				this.next();
				continue;
			}

			// Break if the first char in this line looks like an entry start.
			var first = this.next();
			if (this.isCharIdStart(first) || first is '-' or '#') {
				break;
			}
		}
	}

	public bool isAttributeStart() {
		return this.currentPeek() == '.';
	}

	public char takeIDStart() {
		var ch = this.currentChar();
		if (this.isCharIdStart(ch)) {
			this.next();
			return ch.Value;
		}

		throw new ParseError("E0004", "a-zA-Z");
	}

	public char? takeIDChar()
	{
		return this.takeChar(cc =>
			(cc >= 97 && cc <= 122) || // a-z
			(cc >= 65 && cc <= 90) || // A-Z
			(cc >= 48 && cc <= 57) || // 0-9
			(cc == 95 || cc == 45)); // _-
	}

	public char? takeDigit() {
		return this.takeChar(IsDigit);
	}

	public char? takeHexDigit()
	{
		return this.takeChar(cc =>
			(cc >= 48 && cc <= 57) // 0-9
			|| (cc >= 65 && cc <= 70) // A-F
			|| (cc >= 97 && cc <= 102)); // a-f
	}

	private static bool IsDigit(char cc)
	{
		var c = (int)cc;
		return c is >= 48 and <= 57;
	}
}
