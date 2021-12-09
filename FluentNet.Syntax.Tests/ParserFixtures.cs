using System.IO;
using System.Linq;
using Fluent.Syntax;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;

namespace FluentNet.Syntax.Tests;

public class ParserFixtures
{
	private readonly ITestOutputHelper output;

	public ParserFixtures(ITestOutputHelper output)
	{
		this.output = output;
	}

	private static string ReadFile(string path, bool trim)
	{
		var s = File.ReadAllText(path);
		return trim ? s.Trim() : s;
	}

	[Fact]
	public void ParseFixturesCompare() {
		var files = Directory.GetFiles("./fixtures", "*.ftl");

		foreach (var path in files)
		{
			var isCrlf = path.Contains("crlf");

			var referencePath = path.Replace(".ftl", ".json");
			var referenceFile = ReadFile(referencePath, true);
			var ftlFile = ReadFile(path, false);

			output.WriteLine("Parsing: {0}", path);

			var targetAst = Parser.Parse(ftlFile).Value;

			var refAst = JsonConvert.DeserializeObject<Resource<string>>(referenceFile);

			// adapt_ast(&mut ref_ast, isCrlf);

			Assert.Equal(refAst.Body.Count, targetAst.Body.Count);
			foreach (var (entry, refEntry) in targetAst.Body.Zip(refAst.Body)) {
				Assert.Equal(refEntry, entry);
			}
		}
	}

	[Fact]
	public void ParseFixtures()
	{
		var files = Directory.GetFiles("./fixtures", "*.ftl");

		foreach (var path in files)
		{
			output.WriteLine("Attempting to parse file: {0}", path);

			var content = ReadFile(path, false);

			var _ = Parser.Parse(content);
		}
	}
}
