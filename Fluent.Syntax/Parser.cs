using System.Text.RegularExpressions;

namespace Fluent.Syntax;

public class FluentParserOptions
{
	public bool? WithSpans { get; set; }
}

public class FluentParser
{
	private readonly bool withSpans;

	private static Regex trailingWSRe = new Regex("[ \t\n\r]+$", RegexOptions.Compiled);

	public FluentParser(FluentParserOptions? options = null)
	{
		this.withSpans = options?.WithSpans ?? false;
	}

	public AST.Resource parse (string source) {
		var ps = new FluentParserStream(source);
		ps.skipBlankBlock();

		List<AST.Entry> entries = new ();
		AST.Comment? lastComment = null;

		while (ps.currentChar() is {}) {
			var entry = this.getEntryOrJunk(ps);
			var blankLines = ps.skipBlankBlock();

			// Regular Comments require special logic. Comments may be attached to
			// Messages or Terms if they are followed immediately by them. However
			// they should parse as standalone when they're followed by Junk.
			// Consequently, we only attach Comments once we know that the Message
			// or the Term parsed successfully.
			if (entry is AST.Comment commentEntry
					&& blankLines.Length == 0
					&& ps.currentChar() is not null) {
				// Stash the comment and decide what to do with it in the next pass.
				lastComment = commentEntry;
				continue;
			}

			if (lastComment is not null) {
				if (entry is AST.Message message) {
					message.Comment = lastComment;
					if (this.withSpans) {
						// eslint-disable-next-line @typescript-eslint/no-non-null-assertion
						message.Span!.Start = message.Comment.Span!.Start;
					}
				} else if (entry is AST.Term term) {
					term.Comment = lastComment;
					if (this.withSpans) {
						// eslint-disable-next-line @typescript-eslint/no-non-null-assertion
						term.Span!.Start = term.Comment.Span!.Start;
					}
				} else {
					entries.Add(lastComment);
				}
				// In either case, the stashed comment has been dealt with; clear it.
				lastComment = null;
			}

			// No special logic for other types of entries.
			entries.Add(entry);
		}

		var res = new AST.Resource(entries);

		if (this.withSpans) {
			res.addSpan(0, ps.index);
		}

		return res;
	}

	AST.Entry getEntryOrJunk(FluentParserStream ps) {
		var entryStartPos = ps.index;

		try {
			var entry = this.getEntry(ps);
			ps.expectLineEnd();
			return entry;
		} catch (Exception err) {
			if (err is not ParseError parseError) {
				throw;
			}

			var errorIndex = ps.index;
			ps.skipToNextEntryStart(entryStartPos);
			var nextEntryStart = ps.index;
			if (nextEntryStart < errorIndex) {
				// The position of the error must be inside of the Junk's span.
				errorIndex = nextEntryStart;
			}

			// Create a Junk instance
			var slice = ps.content[entryStartPos..nextEntryStart];
			var junk = new AST.Junk(slice);
			if (this.withSpans) {
				junk.addSpan(entryStartPos, nextEntryStart);
			}
			var annot = new AST.Annotation(parseError.Code, parseError.Arguments, err.Message);
			annot.addSpan(errorIndex, errorIndex);
			junk.addAnnotation(annot);
			return junk;
		}
	}

	AST.Entry getEntry(FluentParserStream ps) {
		if (ps.currentChar() == '#') {
			return this.getComment(ps);
		}

		if (ps.currentChar() == '-') {
			return this.getTerm(ps);
		}

		if (ps.isIdentifierStart()) {
			return this.getMessage(ps);
		}

		throw new ParseError("E0002");
	}

	AST.BaseComment getComment(FluentParserStream ps) {
		// 0 - comment
		// 1 - group comment
		// 2 - resource comment
		var level = -1;
		var content = "";

		while (true) {
			var i = -1;
			while (ps.currentChar() == '#' && (i < (level == -1 ? 2 : level))) {
				ps.next();
				i++;
			}

			if (level == -1) {
				level = i;
			}

			if (ps.currentChar() != Constants.EOL) {
				ps.expectChar(' ');
				while (ps.takeChar(x => x != Constants.EOL) is { } ch) {
					content += ch;
				}
			}

			if (ps.isNextLineComment(level)) {
				content += ps.currentChar();
				ps.next();
			} else {
				break;
			}
		}

		return level switch
		{
			0 => new AST.Comment(content),
			1 => new AST.GroupComment(content),
			_ => new AST.ResourceComment(content)
		};
	}

	AST.Message getMessage(FluentParserStream ps) {
		var id = this.getIdentifier(ps);

		ps.skipBlankInline();
		ps.expectChar('=');

		var value = this.maybeGetPattern(ps);
		var attrs = this.getAttributes(ps);

		if (value == null && attrs.Count == 0) {
			throw new ParseError("E0005", id.Name);
		}

		return new AST.Message(id, value, attrs);
	}

	// maybeGetPattern distinguishes between patterns which start on the same line
	// as the identifier (a.k.a. inline signleline patterns and inline multiline
	// patterns) and patterns which start on a new line (a.k.a. block multiline
	// patterns). The distinction is important for the dedentation logic: the
	// indent of the first line of a block pattern must be taken into account when
	// calculating the maximum common indent.
	AST.Pattern? maybeGetPattern(FluentParserStream ps) {
		ps.peekBlankInline();
		if (ps.isValueStart()) {
			ps.skipToPeek();
			return this.getPattern(ps, false);
		}

		ps.peekBlankBlock();
		if (ps.isValueContinuation()) {
			ps.skipToPeek();
			return this.getPattern(ps, true);
		}

		return null;
	}

	AST.Pattern getPattern(FluentParserStream ps, bool isBlock) {
		var elements = new List<IIndentOrPattern>();
		int commonIndentLength;
		if (isBlock) {
			// A block pattern is a pattern which starts on a new line. Store and
			// measure the indent of this first line for the dedentation logic.
			var blankStart = ps.index;
			var firstIndent = ps.skipBlankInline();
			elements.Add(this.getIndent(ps, firstIndent, blankStart));
			commonIndentLength = firstIndent.Length;
		} else {
			commonIndentLength = int.MaxValue;
		}

		while (ps.currentChar() is {} ch) {
			switch (ch) {
				case Constants.EOL: {
					var blankStart = ps.index;
					var blankLines = ps.peekBlankBlock();
					if (ps.isValueContinuation()) {
						ps.skipToPeek();
						var indent = ps.skipBlankInline();
						commonIndentLength = Math.Min(commonIndentLength, indent.Length);
						elements.Add(this.getIndent(ps, blankLines + indent, blankStart));
						break;
					}

					// The end condition for getPattern's while loop is a newline
					// which is not followed by a valid pattern continuation.
					ps.resetPeek();
					goto endLoop;
				}
				case '{':
					elements.Add(this.getPlaceable(ps));
					break;
				case '}':
					throw new ParseError("E0027");
				default:
					elements.Add(this.getTextElement(ps));
					break;
			}
		}
		endLoop:

		var dedented = this.dedent(elements, commonIndentLength);
		return new AST.Pattern(dedented);
	}

	// Create a token representing an indent. It's not part of the AST and it will
	// be trimmed and merged into adjacent TextElements, or turned into a new
	// TextElement, if it's surrounded by two Placeables.
	Indent getIndent(FluentParserStream ps, string value, int start) {
		return new Indent(value, start, ps.index);
	}

	// Dedent a list of elements by removing the maximum common indent from the
	// beginning of text lines. The common indent is calculated in getPattern.
	IReadOnlyCollection<AST.IPatternElement> dedent(
		List<IIndentOrPattern> elements,
		int commonIndent
	) {
		var trimmed = new List<AST.IPatternElement>();

		foreach (var element in elements) {
			if (element is AST.Placeable placeable) {
				trimmed.Add(placeable);
				continue;
			}

			if (element is Indent indent) {
				// Strip common indent.
				indent.Value = indent.Value[..^commonIndent];
				if (indent.Value.Length == 0) {
					continue;
				}
			}

			var prev = trimmed.LastOrDefault();
			if (prev is AST.TextElement textElement) {
				var el = (AST.IIndentOrTextElement)element;

				// Join adjacent TextElements by replacing them with their sum.
				var sum = new AST.TextElement(textElement.Value + el.Value);
				if (this.withSpans) {
					// eslint-disable-next-line @typescript-eslint/no-non-null-assertion
					sum.addSpan(textElement.Span!.Start, el.Span!.End);
				}
				trimmed[^1] = sum;
				continue;
			}

			if (element is Indent remainingIndent) {
				// If the indent hasn't been merged into a preceding TextElement,
				// convert it into a new TextElement.
				var newTextElement = new AST.TextElement(remainingIndent.Value);
				if (this.withSpans) {
					newTextElement.addSpan(remainingIndent.Span.Start, remainingIndent.Span.End);
				}

				trimmed.Add(newTextElement);
			} else if (element is AST.IPatternElement pattern) {
				trimmed.Add(pattern);
			} else {
				throw new Exception("Should not be reached");
			}
		}

		// Trim trailing whitespace from the Pattern.
		var lastElement = trimmed.LastOrDefault();
		if (lastElement is AST.TextElement lastTextElement) {
			lastTextElement.Value = trailingWSRe.Replace(lastTextElement.Value, "");
			if (lastTextElement.Value.Length == 0) {
				trimmed.RemoveAt(trimmed.Count - 1);
			}
		}

		return trimmed;
	}

	AST.TextElement getTextElement(FluentParserStream ps) {
		var buffer = "";

		while (ps.currentChar() is {} ch) {
			if (ch == '{' || ch == '}') {
				return new AST.TextElement(buffer);
			}

			if (ch == Constants.EOL) {
				return new AST.TextElement(buffer);
			}

			buffer += ch;
			ps.next();
		}

		return new AST.TextElement(buffer);
	}

	string getEscapeSequence(FluentParserStream ps) {
		var next = ps.currentChar();

		switch (next) {
			case '\\':
			case '\"':
				ps.next();
				return $"\\{next.Value}";
			case 'u':
				return this.getUnicodeEscapeSequence(ps, next.Value, 4);
			case 'U':
				return this.getUnicodeEscapeSequence(ps, next.Value, 6);
			default:
				throw new ParseError("E0025", next);
		}
	}

	string getUnicodeEscapeSequence(
		FluentParserStream ps,
		char u,
		int digits
	) {
		ps.expectChar(u);

		var sequence = "";
		for (var i = 0; i < digits; i++) {
			var ch = ps.takeHexDigit();

			if (ch == null) {
				throw new ParseError(
					"E0026", $"\\{u}{sequence}{ps.currentChar()}");
			}

			sequence += ch;
		}

		return $"\\{u}{sequence}";
	}

	AST.Term getTerm(FluentParserStream ps) {
		ps.expectChar('-');
		var id = this.getIdentifier(ps);

		ps.skipBlankInline();
		ps.expectChar('=');

		var value = this.maybeGetPattern(ps);
		if (value == null) {
			throw new ParseError("E0006", id.Name);
		}

		var attrs = this.getAttributes(ps);
		return new AST.Term(id, value, attrs);
	}

	AST.Attribute getAttribute(FluentParserStream ps) {
		var attributeStartPos = ps.index;
		
		ps.expectChar('.');

		var key = this.getIdentifier(ps);

		ps.skipBlankInline();
		ps.expectChar('=');

		var value = this.maybeGetPattern(ps);
		if (value == null) {
			throw new ParseError("E0012");
		}

		return new AST.Attribute(key, value);
	}

	IReadOnlyCollection<AST.Attribute> getAttributes(FluentParserStream ps) {
		var attrs = new List<AST.Attribute>();
		ps.peekBlank();
		while (ps.isAttributeStart()) {
			ps.skipToPeek();

			var attr = this.getAttribute(ps);
			attrs.Add(attr);
			ps.peekBlank();
		}
		return attrs;
	}

	AST.Identifier getIdentifier(FluentParserStream ps) {
		var name = ps.takeIDStart().ToString();

		while (ps.takeIDChar() is {} ch) {
			name += ch;
		}

		return new AST.Identifier(name);
	}

	AST.IVariantKey getVariantKey(FluentParserStream ps) {
		var ch = ps.currentChar();

		if (ch == null) {
			throw new ParseError("E0013");
		}

		var cc = ch.Value;

		if (char.IsNumber(cc) || cc == 45) { // 0-9, -
			return this.getNumber(ps);
		}

		return this.getIdentifier(ps);
	}

	AST.Variant getVariant(FluentParserStream ps, bool hasDefault = false) {
		var defaultIndex = false;

		if (ps.currentChar() == '*') {
			if (hasDefault) {
				throw new ParseError("E0015");
			}
			ps.next();
			defaultIndex = true;
		}

		ps.expectChar('[');

		ps.skipBlank();

		var key = this.getVariantKey(ps);

		ps.skipBlank();
		ps.expectChar(']');

		var value = this.maybeGetPattern(ps);
		if (value == null) {
			throw new ParseError("E0012");
		}

		return new AST.Variant(key, value, defaultIndex);
	}

	IReadOnlyCollection<AST.Variant> getVariants(FluentParserStream ps) {
		var variants = new List<AST.Variant>();
		var hasDefault = false;

		ps.skipBlank();
		while (ps.isVariantStart()) {
			var variant = this.getVariant(ps, hasDefault);

			if (variant.Default) {
				hasDefault = true;
			}

			variants.Add(variant);
			ps.expectLineEnd();
			ps.skipBlank();
		}

		if (variants.Count == 0) {
			throw new ParseError("E0011");
		}

		if (!hasDefault) {
			throw new ParseError("E0010");
		}

		return variants;
	}

	string getDigits(FluentParserStream ps) {
		var num = "";

		while (ps.takeDigit() is {} ch) {
			num += ch;
		}

		if (num.Length == 0) {
			throw new ParseError("E0004", "0-9");
		}

		return num;
	}

	AST.NumberLiteral getNumber(FluentParserStream ps) {
		var value = "";

		if (ps.currentChar() == '-') {
			ps.next();
			value += $"-{this.getDigits(ps)}";
		} else {
			value += this.getDigits(ps);
		}

		if (ps.currentChar() == '.') {
			ps.next();
			value += $".{this.getDigits(ps)}";
		}

		return new AST.NumberLiteral(value);
	}

	AST.Placeable getPlaceable(FluentParserStream ps) {
		ps.expectChar('{');
		ps.skipBlank();
		var expression = this.getExpression(ps);
		ps.expectChar('}');
		return new AST.Placeable(expression);
	}

	AST.IExpression getExpression(FluentParserStream ps) {
		var selector = this.getInlineExpression(ps);
		ps.skipBlank();

		if (ps.currentChar() == '-') {
			if (ps.peek() != '>') {
				ps.resetPeek();
				return selector;
			}

			// Validate selector expression according to
			// abstract.js in the Fluent specification

			if (selector is AST.MessageReference messageReference) {
				if (messageReference.Attribute == null) {
					throw new ParseError("E0016");
				} else {
					throw new ParseError("E0018");
				}
			} else if (selector is AST.TermReference termReference) {
				if (termReference.Attribute == null) {
					throw new ParseError("E0017");
				}
			} else if (selector is AST.Placeable) {
				throw new ParseError("E0029");
			}

			ps.next();
			ps.next();

			ps.skipBlankInline();
			ps.expectLineEnd();

			var variants = this.getVariants(ps);
			return new AST.SelectExpression(selector, variants);
		}

		if (selector is AST.TermReference { Attribute: { } }) {
			throw new ParseError("E0019");
		}

		return selector;
	}

	AST.IInlineExpression getInlineExpression(FluentParserStream ps)
	{
		if (ps.currentChar() == '{') {
			return this.getPlaceable(ps);
		}

		if (ps.isNumberStart()) {
			return this.getNumber(ps);
		}

		if (ps.currentChar() == '"') {
			return this.getString(ps);
		}

		if (ps.currentChar() == '$') {
			ps.next();
			var id = this.getIdentifier(ps);
			return new AST.VariableReference(id);
		}

		if (ps.currentChar() == '-') {
			ps.next();
			var id = this.getIdentifier(ps);

			AST.Identifier? attr = null;
			if (ps.currentChar() == '.') {
				ps.next();
				attr = this.getIdentifier(ps);
			}

			AST.CallArguments? args = null;
			ps.peekBlank();
			if (ps.currentPeek() == '(') {
				ps.skipToPeek();
				args = this.getCallArguments(ps);
			}

			return new AST.TermReference(id, attr, args);
		}

		if (ps.isIdentifierStart()) {
			var id = this.getIdentifier(ps);
			ps.peekBlank();

			if (ps.currentPeek() == '(') {
				// It's a Function. Ensure it's all upper-case.
				if (!new Regex("^[A-Z][A-Z0-9_-]*$").IsMatch(id.Name)) {
					throw new ParseError("E0008");
				}

				ps.skipToPeek();
				var args = this.getCallArguments(ps);
				return new AST.FunctionReference(id, args);
			}

			AST.Identifier? attr = null;
			if (ps.currentChar() == '.') {
				ps.next();
				attr = this.getIdentifier(ps);
			}

			return new AST.MessageReference(id, attr);
		}


		throw new ParseError("E0028");
	}

	AST.ICallArgument getCallArgument(FluentParserStream ps) {
		var exp = this.getInlineExpression(ps);

		ps.skipBlank();

		if (ps.currentChar() != ':') {
			return exp;
		}

		if (exp is AST.MessageReference { Attribute: null } messageReference) {
			ps.next();
			ps.skipBlank();

			var value = this.getLiteral(ps);
			return new AST.NamedArgument(messageReference.Id, value);
		}

		throw new ParseError("E0009");
	}

	AST.CallArguments getCallArguments(FluentParserStream ps) {
		var positional = new List<AST.IInlineExpression>();
		var named = new List<AST.NamedArgument>();
		var argumentNames = new HashSet<string>();

		ps.expectChar('(');
		ps.skipBlank();

		while (true) {
			if (ps.currentChar() == ')') {
				break;
			}

			var arg = this.getCallArgument(ps);
			if (arg is AST.NamedArgument namedArgument) {
				if (argumentNames.Contains(namedArgument.Name.Name)) {
					throw new ParseError("E0022");
				}
				named.Add(namedArgument);
				argumentNames.Add(namedArgument.Name.Name);
			} else if (argumentNames.Count > 0) {
				throw new ParseError("E0021");
			} else {
				positional.Add((AST.IInlineExpression)arg);
			}

			ps.skipBlank();

			if (ps.currentChar() == ',') {
				ps.next();
				ps.skipBlank();
				continue;
			}

			break;
		}

		ps.expectChar(')');
		return new AST.CallArguments(positional, named);
	}

	AST.StringLiteral getString(FluentParserStream ps) {
		ps.expectChar('\"');
		var value = "";

		while (ps.takeChar(x => x != '"' && x != Constants.EOL) is { } ch) {
			if (ch == '\\') {
				value += this.getEscapeSequence(ps);
			} else {
				value += ch;
			}
		}

		if (ps.currentChar() == Constants.EOL) {
			throw new ParseError("E0020");
		}

		ps.expectChar('\"');

		return new AST.StringLiteral(value);
	}

	AST.BaseLiteral getLiteral(FluentParserStream ps) {
		if (ps.isNumberStart()) {
			return this.getNumber(ps);
		}

		if (ps.currentChar() == '"') {
			return this.getString(ps);
		}

		throw new ParseError("E0014");
	}
}

public interface IIndentOrPattern { }

record Indent : IIndentOrPattern, AST.IIndentOrTextElement
{
	public Indent(string value, int start, int index)
	{
		this.Value = value;
		Span = new AST.Span(start, index);
	}

	public string Value { get; set; }

	public AST.Span? Span { get; set; }
}
