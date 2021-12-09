namespace Fluent.Syntax;

public static class Parser
{
	public static ParseResult<Ast> Parse(string data)
	{
		return new ParseResult<Ast>
		{
			Value = new Ast
			{
				Body = new List<Entry>()
			}
		};
	}
}

public class ParseResult<T>
{
	public T Value { get; init; }
}

public class Ast
{
	public IReadOnlyCollection<Entry> Body;
}

public class Resource<T>
{
	public IReadOnlyCollection<Entry> Body;
}

public class Entry {
}
