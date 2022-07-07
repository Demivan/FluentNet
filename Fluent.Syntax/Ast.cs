using Fluent.Syntax;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace AST;

public class BaseSpecifiedConcreteClassConverter : DefaultContractResolver
{
	protected override JsonConverter ResolveContractConverter(Type objectType)
	{
		if (typeof(SyntaxNode).IsAssignableFrom(objectType) && !objectType.IsAbstract)
			return null;

		return base.ResolveContractConverter(objectType);
	}
}

public class BaseClassConverter : JsonConverter<SyntaxNode>
{
	static JsonSerializerSettings SpecifiedSubclassConversion = new JsonSerializerSettings() { ContractResolver = new BaseSpecifiedConcreteClassConverter() };

	private readonly IEnumerable<Type> types;

	public BaseClassConverter()
	{
		var type = typeof(BaseNode);
		types = AppDomain.CurrentDomain.GetAssemblies()
			.SelectMany(s => s.GetTypes())
			.Where(p => type.IsAssignableFrom(p) && p.IsClass && !p.IsAbstract)
			.ToList();
	}

	public override bool CanWrite { get; } = false;

	public override SyntaxNode? ReadJson(JsonReader reader, Type objectType, SyntaxNode? existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
			return null;

		// Load JObject from stream
		JObject jsonObject = JObject.Load(reader);

		// Create target object based on JObject
		var value = jsonObject.GetValue("type")?.ToObject<string>();
		if (value == null) {
			throw new Exception("Wrong type prop");
		}

		var type = types.FirstOrDefault(t => t.Name == value);
		if (type == null) {
			throw new Exception($"Type not found for {value}");
		}

		if (!type.IsAbstract && !type.IsInterface) {
			return (SyntaxNode?)JsonConvert.DeserializeObject(jsonObject.ToString(), type, SpecifiedSubclassConversion);
		}

		return (SyntaxNode?)serializer.Deserialize(jsonObject.CreateReader(), type);
	}

	public override void WriteJson(JsonWriter writer, SyntaxNode? value, JsonSerializer serializer)
	{
		serializer.Serialize(writer, value);
	}
}

public abstract record BaseNode;

[JsonConverter(typeof(BaseClassConverter))]
public interface Entry
{
}

/*
 * Base class for AST nodes which can have Spans.
 */
public abstract record SyntaxNode : BaseNode {
	public abstract string Type { get; }

	public Span? Span { get; private set; }

	public void addSpan(int start, int end) {
		this.Span = new Span(start, end);
	}
}

record Message(Identifier Id, Pattern? Value, IReadOnlyCollection<Attribute> Attributes) : SyntaxNode, Entry
{
	public override string Type => nameof(Message);

	public Comment? Comment { get; set; }
}

record Term(Identifier Id, Pattern? Value, IReadOnlyCollection<Attribute> Attributes) : SyntaxNode, Entry
{
	public override string Type => nameof(Term);

	public Comment? Comment { get; set; }
}

[JsonConverter(typeof(BaseClassConverter))]
public interface IPatternElement : IIndentOrPattern {};

public record TextElement(string Value) : SyntaxNode, IPatternElement, IIndentOrTextElement
{
	public override string Type => nameof(TextElement);

	public string Value { get; set; } = Value;

	public new Span? Span { get; set; }
}

public interface IInlineExpression : IExpression, ICallArgument
{
}

[JsonConverter(typeof(BaseClassConverter))]
public interface IExpression
{
}

[JsonConverter(typeof(BaseClassConverter))]
public abstract record BaseLiteral(string Value) : SyntaxNode
{
};

public record StringLiteral(string Value) : BaseLiteral(Value), IInlineExpression
{
	public override string Type => nameof(StringLiteral);
}

public record NumberLiteral(string Value) : BaseLiteral(Value), IInlineExpression, IVariantKey
{
	public override string Type => nameof(NumberLiteral);
};

public record Placeable(IExpression Expression) : SyntaxNode, IInlineExpression, IPatternElement
{
	public override string Type => nameof(Placeable);
};

public record Pattern(IReadOnlyCollection<IPatternElement> Elements) : SyntaxNode
{
	public override string Type => nameof(Pattern);
};

public record MessageReference(Identifier Id, Identifier? Attribute) : SyntaxNode, IInlineExpression
{
	public override string Type => nameof(MessageReference);
};

public record TermReference(Identifier Id, Identifier? Attribute, CallArguments? Arguments) : SyntaxNode, IInlineExpression
{
	public override string Type => nameof(TermReference);
};

public record VariableReference(Identifier Id) : SyntaxNode, IInlineExpression
{
	public override string Type => nameof(VariableReference);
};

public record FunctionReference(Identifier Id, CallArguments Arguments) : SyntaxNode, IInlineExpression
{
	public override string Type => nameof(FunctionReference);
};

public record SelectExpression(IInlineExpression Selector, IReadOnlyCollection<Variant> Variants) : SyntaxNode,
	IExpression
{
	public override string Type => nameof(SelectExpression);
};

public record CallArguments(
	IReadOnlyCollection<IInlineExpression> Positional,
	IReadOnlyCollection<NamedArgument> Named) : SyntaxNode
{
	public override string Type => nameof(CallArguments);
};

public record Attribute(Identifier Id, Pattern Value) : SyntaxNode
{
	public override string Type => nameof(Attribute);
};

public record Variant(IVariantKey Key, Pattern Value, bool Default) : SyntaxNode
{
	public override string Type => nameof(Variant);
};

public record NamedArgument(Identifier Name, BaseLiteral Value) : SyntaxNode, ICallArgument
{
	public override string Type => nameof(NamedArgument);
};

[JsonConverter(typeof(BaseClassConverter))]
public interface IVariantKey
{
}

public interface ICallArgument
{
}

public record Identifier(string Name) : SyntaxNode, IVariantKey
{
	public override string Type => nameof(Identifier);
}

public abstract record BaseComment(string Content) : SyntaxNode, Entry;

public record Comment(string Content) : BaseComment(Content), Entry
{
	public override string Type => nameof(Comment);
};

public record GroupComment(string Content) : BaseComment(Content), Entry
{
	public override string Type => nameof(GroupComment);
};

public record ResourceComment(string Content) : BaseComment(Content), Entry
{
	public override string Type => nameof(ResourceComment);
};

public record Resource(IReadOnlyCollection<Entry> Body) : SyntaxNode
{
	public override string Type => nameof(Resource);

};

public record Junk(string Content): SyntaxNode, Entry {
	public override string Type => nameof(Junk);

	public List<Annotation> Annotations { get; set; } = new();

	public void addAnnotation(Annotation annotation) {
		this.Annotations.Add(annotation);
	}
}

public record Span(int Start, int End) : BaseNode
{
	public int Start { get; set; } = Start;

	public int End { get; set; } = End;
}

public record Annotation(string Code, IReadOnlyCollection<object> Arguments, string Message) : SyntaxNode
{
	public override string Type => nameof(Annotation);
};


interface IIndentOrTextElement
{
	Span? Span { get; set; }

	string Value { get; set; }
}
