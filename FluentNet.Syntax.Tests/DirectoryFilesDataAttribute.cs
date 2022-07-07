using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit.Sdk;

namespace FluentNet.Syntax.Tests;

/// <summary>
/// Data attribute 
/// </summary>
public class DirectoryFilesDataAttribute : DataAttribute
{
	private readonly string pattern;
	private readonly string rootDir;

	/// <summary>
	/// Load data from a JSON file as the data source for a theory
	/// </summary>
	/// <param name="pattern">File pattern to search for.</param>
	/// <param name="rootDir">Root directory.</param>
	public DirectoryFilesDataAttribute(string pattern, string? rootDir = null)
	{
		this.pattern = pattern;
		this.rootDir = rootDir ?? Directory.GetCurrentDirectory();
	}
	
	/// <inheritDoc />
	public override IEnumerable<object[]> GetData(MethodInfo testMethod)
	{
		if (testMethod == null) { throw new ArgumentNullException(nameof(testMethod)); }

		return Directory.EnumerateFiles(rootDir, pattern).Select(file => new [] { file });
	}
}
