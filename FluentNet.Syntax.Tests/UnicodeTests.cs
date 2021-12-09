using Fluent.Syntax;
using Xunit;

namespace FluentNet.Syntax.Tests;

public class UnicodeTests
{
	private static void TestUnescapeUnicode(string input, string output) {
		var s = Unicode.UnescapeUnicode(input);

		Assert.Equal(output, s);
	}

	[Fact]
	public void UnescapeUnicodeTest()
	{
		TestUnescapeUnicode("foo", "foo");
		TestUnescapeUnicode("foo \\\\", "foo \\");
		TestUnescapeUnicode("foo \\\"", "foo \"");
		TestUnescapeUnicode("foo \\\\ faa", "foo \\ faa");
		TestUnescapeUnicode("foo \\\\ faa \\\\ fii", "foo \\ faa \\ fii");
		TestUnescapeUnicode("foo \\\\\\\" faa \\\"\\\\ fii", "foo \\\" faa \"\\ fii");
		TestUnescapeUnicode("\\u0041\\u004F", "AO");
		TestUnescapeUnicode("\\uA", "�");
		TestUnescapeUnicode("\\uA0Pl", "�");
		TestUnescapeUnicode("\\d Foo", "� Foo");
	}
}
