using RegexDebug.RegexDev;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;

namespace RegexDebug
{
	internal class RegexNodeJson
	{
		internal string GetJsonObject(IRegexNode node)
		{
			var options = new JsonWriterOptions
			{
				Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
				//Indented = true 
			};

			var buffer = new ArrayBufferWriter<byte>();
			var writer = new Utf8JsonWriter(buffer, options);
			GetJsonObject(node, writer);
			writer.Flush();
			return Encoding.UTF8.GetString(buffer.WrittenSpan);
		}

		private void GetJsonObject(IRegexNode node, Utf8JsonWriter writer)
		{
			writer.WriteStartObject();
			if (node is RePanel panel)
			{
				var type = "sequence";
				if (panel.addBracket)
					type = "constructure";
				else if (panel.GroupingConstruct.Count > 0)
					type = "constructure";
				else if (!string.IsNullOrEmpty(panel.Quantifier))
					type = "repeat";

				writer.WriteString("type", type);

				if (type == "sequence")
				{
					writer.WriteStartArray("children");
					foreach (var child in panel.SequenceNodes)
					{
						GetJsonObject(child, writer);
					}
					writer.WriteEndArray();
				}
				else
				{
					writer.WritePropertyName("child");
					GetJsonObject(panel.SequenceNodes[0], writer);
				}

				if (panel.addBracket)
				{
					writer.WriteString("pattern", "(");
					if (panel.GroupingNumber > 0)
						writer.WriteNumber("GroupingNumber", panel.GroupingNumber);
				}
				else if (panel.GroupingConstruct.Count > 0)
				{
					writer.WriteString("pattern", panel.GroupingConstruct[0]);
					if (panel.GroupingNumber > 0)
						writer.WriteNumber("GroupingNumber", panel.GroupingNumber);
					if (panel.CaptureGroup2rdName != "")
						writer.WriteString("CaptureGroup2rdName", panel.CaptureGroup2rdName);
					if (panel.BalancingGroup2rdName != "")
						writer.WriteString("BalancingGroup2rdName", panel.BalancingGroup2rdName);
				}
				else if (panel.Quantifier.Length > 0)
					writer.WriteString("kind", panel.Quantifier);

			}
			else if (node is Reline line)
			{
				writer.WriteString("type", "alt");
				writer.WriteStartArray("children");
				foreach (var child in line.AlternationNodes)
				{
					GetJsonObject(child, writer);
				}
				writer.WriteEndArray();
			}
			else if (node is ReSingle single)
			{
				writer.WriteString("type", "single");
				writer.WriteString("text", single.pattern);
				if (single.IsReference != null)
					writer.WriteBoolean("IsReference", single.IsReference == true);
				if (single.singleType == SingleType.EndofLineComment) writer.WriteNumber("EndofLineComment", 1);
			}
			else if (node is ReCondition condition)
			{
				writer.WriteString("type", "condition");

				if (!condition.HaveNoBanch)
					writer.WriteNumber("notHaveNoBanch", 1);

				if (condition.condition1 != null)
				{
					writer.WritePropertyName("c1");
					GetJsonObject(condition.condition1, writer);
				}
				else
				{
					writer.WriteString("group", condition.conditionGroup);
					if (condition.conditionGroup2RdName.Length > 0)
						writer.WriteString("group2rdName", condition.conditionGroup2RdName);
				}

				writer.WritePropertyName("c2");
				GetJsonObject(condition.condition2, writer);

				writer.WritePropertyName("c3");
				GetJsonObject(condition.condition3, writer);
			}
			else throw new Exception("unkonw regex node");

			writer.WriteEndObject();
		}
	}
}
