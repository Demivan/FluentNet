using System.Text;

namespace Fluent.Syntax;

public static class Unicode
{
	private const char UNKNOWN_CHAR = '�';

	private static char EncodeUnicode(string s)
	{
		try
		{
			return Convert.ToChar(Convert.ToUInt32(s, 16));
		}
		catch
		{
			return UNKNOWN_CHAR;
		}
	}

	private static char GetUnicodeChar(string input, char u, ref int ptr)
	{
		var seqStart = ptr + 1;
		var len = u == 'u' ? 4 : 6;
		ptr += len;
		var end = seqStart + len;

		return input.Length > end - 1 ? EncodeUnicode(input[seqStart..end]) : UNKNOWN_CHAR;
	}

	public static string UnescapeUnicode(string input)
	{
		var bytes = input.AsSpan();

		var start = 0;
		var ptr = 0;

		var result = new StringBuilder();

		while (ptr < bytes.Length)
		{
			var b = bytes[ptr];
			if (b != '\\') {
				ptr += 1;
				continue;
			}

			if (start != ptr) {
				result.Append(input[start..ptr]);
			}

			ptr += 1;

			var newChar = bytes[ptr] switch {
				'\\' => '\\',
				'"' => '"',
				'u' or 'U' => GetUnicodeChar(input, bytes[ptr], ref ptr),
				_ => UNKNOWN_CHAR,
			};
			ptr += 1;

			result.Append(newChar);
			start = ptr;
		}

		if (start != ptr) {
			result.Append(input[start..ptr]);
		}

		return result.ToString();
	}
}
