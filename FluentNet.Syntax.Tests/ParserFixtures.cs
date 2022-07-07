using System.Collections.Generic;
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

	[Theory]
	[DirectoryFilesData("*.ftl", "./fixtures")]
	public void ParseFixturesCompare(string path) {
		var referencePath = path.Replace(".ftl", ".json");
		var referenceFile = ReadFile(referencePath, true);
		var ftlFile = ReadFile(path, false);

		var skips = new[]
		{
			// Broken Attributes break the entire Entry right now.
			// https://github.com/projectfluent/fluent.js/issues/237
			"leading_dots.ftl",
		};

		if (skips.Any(path.Contains)) {
			return;
		}

		var parsedAst = new FluentParser().parse(ftlFile);

		var refAst = JsonConvert.DeserializeObject<AST.Resource>(referenceFile);

		foreach (var entry in parsedAst.Body) {
			if (entry is AST.Junk junk)
				junk.Annotations = new List<AST.Annotation>();
		}

		Assert.Equal(refAst.Body.Count, parsedAst.Body.Count);
		foreach (var (refMessage, parsedMessage) in refAst.Body.Zip(parsedAst.Body)) {
			var expected = JsonConvert.SerializeObject(refMessage, Formatting.Indented);
			var actual = JsonConvert.SerializeObject(parsedMessage, Formatting.Indented);
			Assert.Equal(expected, actual);
		}
	}

	[Theory]
	[DirectoryFilesData("*.ftl", "./fixtures")]
	public void ParseFixtures(string path)
	{
			var content = ReadFile(path, false);

			var res = new FluentParser().parse(content);
			
			Assert.True(true);
	}
}
