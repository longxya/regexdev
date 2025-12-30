using RegexDebug.RegexDev;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace RegexDebug
{
    internal class Program
    {
        static void Main(string[] args)
        {
			var pattern = @"(?x:#this ia a regex to quickly match any very long palindrome string
(?=^(?'len'.)+)(?=(?(h)(?'-h')(?'half')(?'-len').|(?'h')(?'-len').)+)(?'-h')?(?'half')#first to obtain half of the length of the text to be matched
(?i)
  (?'-half'(.))+#balancing group#half is used to ensure that the left side only moustly matches half of the target string, avoiding invalid backtracking
  (.)?(?'-1'\1)+(?!(?'-1'))
)";
			var astStyle = 1;//whether or not convert AST to .NET 5+ compatible regex pattern
			var regexCanRunOnDotNET5 = "";
			var rootNode = new RegexParse().ParseAST(pattern, astStyle, out regexCanRunOnDotNET5);
			//print colorful AST tree
			Console.WriteLine(regexCanRunOnDotNET5);
			RegexParserUtils.PrintColorASTTree(rootNode);


			Test();
		}

		private static void Test()
		{
			int customBufferSize = 10000 * 10;
			Console.SetIn(new StreamReader(
				Console.OpenStandardInput(customBufferSize),
				Console.InputEncoding,
				false,
				customBufferSize
			));

			Console.WriteLine("Pause your regex, and input 'parse' in new line to parse your regex:");

			var pattern = Console.ReadLine();
			while (true)
			{
				#region read console input, bulid a regex pattern
				while (true)
				{
					var text = Console.ReadLine();
					if (text.ToUpper() == "EXIT") Environment.Exit(0);
					else if (text.ToUpper() == "CLS" || text.ToUpper() == "CLEAR")
					{
						pattern = "";
						Console.Clear();
					}
					else if (text.ToUpper() == "PARSE") break;
					else
					{
						if (pattern.Length > 0) pattern += "\n";
						pattern += text;
					}
				}
				#endregion

				var patterndotNET5 = "";
				try
				{
					Stopwatch stopwatch = Stopwatch.StartNew();
					var parse = new RegexParse();
					var rootNode = parse.ParseAST(pattern, 1, out patterndotNET5);
					stopwatch.Stop();
					var myParseTime = stopwatch.Elapsed.TotalMilliseconds;
					stopwatch.Restart();
					new Regex(patterndotNET5);
					stopwatch.Stop();
					var dotNETParseTime = stopwatch.Elapsed.TotalMilliseconds;


					//Test parse regex if or not equal to original pattern
					var sb = new StringBuilder();
					RegexParse.GetUnitPattern(rootNode, sb);//convert AST back to regex pattern, to verify the parsing is correct
					patterndotNET5 = sb.ToString();

					Console.ForegroundColor = ConsoleColor.Magenta;
					Console.Write("Parse pattern is equal original pattern:");
					if (patterndotNET5 == pattern)//not equal doesn't mean parsing wrong, it is very highly likely that the regex has been converted
						Console.ForegroundColor = ConsoleColor.Blue;
					else Console.ForegroundColor = ConsoleColor.DarkRed;
					Console.WriteLine((patterndotNET5 == pattern) + (patterndotNET5 == pattern ? "" : "\tnot equal doesn't mean parsing wrong, it is very highly likely that the regex has been converted"));
					Console.ForegroundColor = ConsoleColor.White;
					if (patterndotNET5 != pattern)
					{
						Console.ForegroundColor = ConsoleColor.Red;
						Console.WriteLine(patterndotNET5);
						Console.ForegroundColor = ConsoleColor.White;
					}

					// print AST
					//RegexParserUtils.PrintAST(rootNode);
					// doubao AI's printer：
					//RegexASTTreePrinter.PrintASTTree(rootNode);

					// Print AST TREE
					//RegexParserUtils.PrintASTTree(rootNode);

					//print colorful AST tree
					RegexParserUtils.PrintColorASTTree(rootNode);

					// generate Markdown style mermaid diagram
					//string markdown = RegexParserUtils.ASTToStyledMermaid(rootNode);
					//Console.WriteLine(markdown);

					// generate node diagram in HTML, and open it in default browser
					{
						string html = RegexParserUtils.ASTToHtml(rootNode, patterndotNET5);
						File.WriteAllText("regex_ast.html", html);//save to current directory
						string fileName = "regex_ast.html";
						string filePath = Path.Combine(Directory.GetCurrentDirectory(), fileName);
						ProcessStartInfo psi = new ProcessStartInfo
						{
							FileName = "cmd.exe",
							Arguments = $"/c start \"\" \"{filePath}\"",
							CreateNoWindow = true,
							UseShellExecute = false
						};
						Process.Start(psi);
					}

					Console.WriteLine($"ParseAST:{myParseTime}(match:{parse.regexMatchTime}),c# engine:{dotNETParseTime}");
				}
				catch (Exception ex)
				{
					Console.ForegroundColor = ConsoleColor.DarkRed;
					Console.WriteLine(ex.Message);
					Console.ForegroundColor = ConsoleColor.White;
				}
				pattern = "";
				Console.WriteLine();
			}
		}


    }
}
