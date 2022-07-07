using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.CSharp.Testing.XUnit;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace Fluent.SourceGeneratorTests;

public class FluentAdditionalText : AdditionalText
{
	private readonly string text;

	public override string Path { get; }

	public FluentAdditionalText(string name,string text)
	{
		Path = $"{name}.ftl";
		this.text = text;
	}

	public override SourceText GetText(CancellationToken cancellationToken = new())
	{
		return SourceText.From(text);
	}
}

public static class TestUtils
{
	public static CSharpCompilation RunGenerator(IEnumerable<AdditionalText> additionalFiles)
	{
		var compilation = CSharpCompilation.Create("MyCompilation");
		
		var driver = CSharpGeneratorDriver.Create(
			new []{ new SourceGenerator.SourceGenerator() },
			additionalFiles,
			new CSharpParseOptions());

		driver.RunGenerators(compilation);

		return compilation;
	}
}

public class SourceGeneratorTests
{
	[Fact]
	public async Task Test()
	{
		var source = "";

		var compilation = TestUtils.RunGenerator(new[]
		{
			new FluentAdditionalText("test", "key = value")
		});
	}
}
