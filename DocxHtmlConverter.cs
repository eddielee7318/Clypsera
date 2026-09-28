using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace ClipboardTrail
{
    // Converts the useful WordprocessingML structure to safe, offline HTML.
    // Keeping paragraph/run styles here is important: flattening DOCX to InnerText
    // makes teaching material, tables and paired-language layouts look corrupted.
    internal static class DocxHtmlConverter
    {
        private const string WordNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

        private sealed class WordStyle
        {
            public string Id;
            public string Name;
            public string Css;
        }

        internal static string Convert(ZipArchive archive)
        {
            ZipArchiveEntry documentEntry = archive.GetEntry("word/document.xml");
            if (documentEntry == null) throw new InvalidDataException("DOCX 中缺少 word/document.xml。");

            XmlDocument document = LoadXml(documentEntry);
            Dictionary<string, WordStyle> styles = LoadStyles(archive);
            XmlNode bodyNode = document.SelectSingleNode("//*[local-name()='body']");
            if (bodyNode == null) throw new InvalidDataException("DOCX 中没有可读取的正文。");

            StringBuilder html = new StringBuilder();
            html.Append("<style id='clypsera-word-styles'>")
                .Append(".word-document{font-family:'Microsoft YaHei UI','Segoe UI',sans-serif;line-height:1.55;max-width:100%;}")
                .Append(".word-document p{white-space:normal;overflow-wrap:anywhere;}")
                .Append(".word-document table{width:100%;border-collapse:collapse;margin:1em 0;table-layout:auto;}")
                .Append(".word-document td,.word-document th{border:1px solid #cfd5df;padding:.5em .65em;vertical-align:top;}")
                .Append(".word-document td p,.word-document th p{margin:.22em 0;}")
                .Append(".word-tab{display:inline-block;width:2.4em;}")
                .Append(".word-page-break{display:block;height:1px;border-top:1px dashed #c8ced8;margin:1.6em 0;}")
                .Append(".word-subtitle{opacity:.78;}")
                .Append(BuildStylesCss(styles))
                .Append("</style><article class='word-document'>");

            foreach (XmlNode child in bodyNode.ChildNodes)
            {
                if (child.LocalName == "p") html.Append(RenderParagraph(child, styles));
                else if (child.LocalName == "tbl") html.Append(RenderTable(child, styles));
            }
            html.Append("</article>");
            return html.ToString();
        }

        private static XmlDocument LoadXml(ZipArchiveEntry entry)
        {
            XmlDocument xml = new XmlDocument();
            xml.XmlResolver = null;
            using (Stream stream = entry.Open()) xml.Load(stream);
            return xml;
        }

        private static Dictionary<string, WordStyle> LoadStyles(ZipArchive archive)
        {
            Dictionary<string, WordStyle> result = new Dictionary<string, WordStyle>(StringComparer.OrdinalIgnoreCase);
            ZipArchiveEntry entry = archive.GetEntry("word/styles.xml");
            if (entry == null) return result;
            XmlDocument xml = LoadXml(entry);
            foreach (XmlNode styleNode in xml.SelectNodes("//*[local-name()='style']"))
            {
                string id = Attr(styleNode, "styleId");
                if (string.IsNullOrWhiteSpace(id)) continue;
                XmlNode nameNode = styleNode.SelectSingleNode("./*[local-name()='name']");
                XmlNode paragraphProperties = styleNode.SelectSingleNode("./*[local-name()='pPr']");
                XmlNode runProperties = styleNode.SelectSingleNode("./*[local-name()='rPr']");
                result[id] = new WordStyle
                {
                    Id = id,
                    Name = nameNode == null ? id : Attr(nameNode, "val"),
                    Css = BuildParagraphCss(paragraphProperties) + BuildRunCss(runProperties)
                };
            }
            return result;
        }

        private static string BuildStylesCss(Dictionary<string, WordStyle> styles)
        {
            StringBuilder css = new StringBuilder();
            foreach (WordStyle style in styles.Values)
            {
                if (!string.IsNullOrWhiteSpace(style.Css)) css.Append(".word-style-").Append(CssToken(style.Id)).Append('{').Append(style.Css).Append('}');
            }
            return css.ToString();
        }

        private static string RenderParagraph(XmlNode paragraph, Dictionary<string, WordStyle> styles)
        {
            XmlNode properties = paragraph.SelectSingleNode("./*[local-name()='pPr']");
            XmlNode styleNode = properties == null ? null : properties.SelectSingleNode("./*[local-name()='pStyle']");
            string styleId = styleNode == null ? "" : Attr(styleNode, "val");
            WordStyle style;
            styles.TryGetValue(styleId, out style);
            string styleName = style == null ? "" : (style.Name ?? "");
            string tag = ParagraphTag(styleName);
            string cssClass = "word-p";
            if (!string.IsNullOrWhiteSpace(styleId)) cssClass += " word-style-" + CssToken(styleId);
            if (styleName.Equals("Title", StringComparison.OrdinalIgnoreCase)) cssClass += " word-title";
            if (styleName.Equals("Subtitle", StringComparison.OrdinalIgnoreCase)) cssClass += " word-subtitle";
            if (properties != null && properties.SelectSingleNode("./*[local-name()='pageBreakBefore']") != null) cssClass += " word-break-before";

            StringBuilder content = new StringBuilder();
            RenderInlineChildren(paragraph, content);
            if (content.Length == 0) content.Append("&nbsp;");
            string directCss = BuildParagraphCss(properties);
            return "<" + tag + " class='" + cssClass + "'" + (directCss.Length == 0 ? "" : " style='" + WebUtility.HtmlEncode(directCss) + "'") + ">" + content + "</" + tag + ">";
        }

        private static string ParagraphTag(string styleName)
        {
            if (styleName.Equals("Title", StringComparison.OrdinalIgnoreCase)) return "h1";
            Match heading = Regex.Match(styleName ?? "", @"^heading\s*([1-9])$", RegexOptions.IgnoreCase);
            if (heading.Success) return "h" + Math.Min(6, int.Parse(heading.Groups[1].Value, CultureInfo.InvariantCulture));
            return "p";
        }

        private static void RenderInlineChildren(XmlNode parent, StringBuilder html)
        {
            foreach (XmlNode child in parent.ChildNodes)
            {
                if (child.LocalName == "r") RenderRun(child, html);
                else if (child.LocalName == "hyperlink" || child.LocalName == "smartTag" || child.LocalName == "sdt" || child.LocalName == "sdtContent" || child.LocalName == "fldSimple" || child.LocalName == "ins" || child.LocalName == "moveTo") RenderInlineChildren(child, html);
            }
        }

        private static void RenderRun(XmlNode run, StringBuilder html)
        {
            XmlNode properties = run.SelectSingleNode("./*[local-name()='rPr']");
            if (properties != null && IsOn(properties.SelectSingleNode("./*[local-name()='vanish']"))) return;
            XmlNode runStyle = properties == null ? null : properties.SelectSingleNode("./*[local-name()='rStyle']");
            string runStyleId = runStyle == null ? "" : Attr(runStyle, "val");
            string css = BuildRunCss(properties);
            bool wrapped = css.Length > 0 || runStyleId.Length > 0;
            if (wrapped)
            {
                html.Append("<span");
                if (runStyleId.Length > 0) html.Append(" class='word-style-").Append(CssToken(runStyleId)).Append("'");
                if (css.Length > 0) html.Append(" style='").Append(WebUtility.HtmlEncode(css)).Append("'");
                html.Append('>');
            }
            foreach (XmlNode child in run.ChildNodes)
            {
                if (child.LocalName == "t") html.Append(WebUtility.HtmlEncode(RepairFragment(child.InnerText)));
                else if (child.LocalName == "tab") html.Append("<span class='word-tab'></span>");
                else if (child.LocalName == "cr") html.Append("<br>");
                else if (child.LocalName == "br")
                {
                    string type = Attr(child, "type");
                    html.Append(type == "page" ? "<span class='word-page-break'></span>" : "<br>");
                }
                else if (child.LocalName == "noBreakHyphen") html.Append("&#8209;");
                else if (child.LocalName == "softHyphen") html.Append("&shy;");
            }
            if (wrapped) html.Append("</span>");
        }

        private static string RenderTable(XmlNode table, Dictionary<string, WordStyle> styles)
        {
            XmlNode tableProperties = table.SelectSingleNode("./*[local-name()='tblPr']");
            XmlNode tableStyleNode = tableProperties == null ? null : tableProperties.SelectSingleNode("./*[local-name()='tblStyle']");
            string tableStyle = tableStyleNode == null ? "" : Attr(tableStyleNode, "val");
            StringBuilder html = new StringBuilder("<table class='word-table");
            if (tableStyle.Length > 0) html.Append(" word-style-").Append(CssToken(tableStyle));
            html.Append("'><tbody>");
            foreach (XmlNode row in table.SelectNodes("./*[local-name()='tr']"))
            {
                html.Append("<tr>");
                foreach (XmlNode cell in row.SelectNodes("./*[local-name()='tc']"))
                {
                    XmlNode cellProperties = cell.SelectSingleNode("./*[local-name()='tcPr']");
                    XmlNode spanNode = cellProperties == null ? null : cellProperties.SelectSingleNode("./*[local-name()='gridSpan']");
                    int span;
                    int.TryParse(spanNode == null ? "" : Attr(spanNode, "val"), out span);
                    string cellCss = BuildCellCss(cellProperties);
                    html.Append("<td");
                    if (span > 1) html.Append(" colspan='").Append(span).Append("'");
                    if (cellCss.Length > 0) html.Append(" style='").Append(WebUtility.HtmlEncode(cellCss)).Append("'");
                    html.Append('>');
                    foreach (XmlNode child in cell.ChildNodes)
                    {
                        if (child.LocalName == "p") html.Append(RenderParagraph(child, styles));
                        else if (child.LocalName == "tbl") html.Append(RenderTable(child, styles));
                    }
                    html.Append("</td>");
                }
                html.Append("</tr>");
            }
            return html.Append("</tbody></table>").ToString();
        }

        private static string BuildParagraphCss(XmlNode properties)
        {
            if (properties == null) return "";
            StringBuilder css = new StringBuilder();
            XmlNode alignment = properties.SelectSingleNode("./*[local-name()='jc']");
            string align = alignment == null ? "" : Attr(alignment, "val");
            if (align == "center" || align == "right" || align == "left" || align == "justify") css.Append("text-align:").Append(align).Append(';');
            XmlNode spacing = properties.SelectSingleNode("./*[local-name()='spacing']");
            if (spacing != null)
            {
                AppendTwips(css, "margin-top", Attr(spacing, "before"));
                AppendTwips(css, "margin-bottom", Attr(spacing, "after"));
                double line;
                if (double.TryParse(Attr(spacing, "line"), NumberStyles.Any, CultureInfo.InvariantCulture, out line))
                {
                    string rule = Attr(spacing, "lineRule");
                    if (string.IsNullOrEmpty(rule) || rule == "auto") css.Append("line-height:").Append(Math.Max(.8, line / 240.0).ToString("0.###", CultureInfo.InvariantCulture)).Append(';');
                    else css.Append("line-height:").Append((line / 20.0).ToString("0.###", CultureInfo.InvariantCulture)).Append("pt;");
                }
            }
            XmlNode indentation = properties.SelectSingleNode("./*[local-name()='ind']");
            if (indentation != null)
            {
                AppendTwips(css, "margin-left", FirstAttr(indentation, "left", "start"));
                AppendTwips(css, "margin-right", FirstAttr(indentation, "right", "end"));
                string firstLine = Attr(indentation, "firstLine");
                string hanging = Attr(indentation, "hanging");
                if (!string.IsNullOrEmpty(firstLine)) AppendTwips(css, "text-indent", firstLine);
                else
                {
                    double value;
                    if (double.TryParse(hanging, NumberStyles.Any, CultureInfo.InvariantCulture, out value)) css.Append("text-indent:-").Append((value / 20.0).ToString("0.###", CultureInfo.InvariantCulture)).Append("pt;");
                }
            }
            XmlNode shading = properties.SelectSingleNode("./*[local-name()='shd']");
            AppendColor(css, "background-color", shading == null ? "" : Attr(shading, "fill"));
            AppendBorder(css, properties, "top");
            AppendBorder(css, properties, "bottom");
            if (properties.SelectSingleNode("./*[local-name()='keepNext']") != null) css.Append("break-after:avoid;");
            return css.ToString();
        }

        private static string BuildRunCss(XmlNode properties)
        {
            if (properties == null) return "";
            StringBuilder css = new StringBuilder();
            if (IsOn(properties.SelectSingleNode("./*[local-name()='b']"))) css.Append("font-weight:700;");
            if (IsOn(properties.SelectSingleNode("./*[local-name()='i']"))) css.Append("font-style:italic;");
            XmlNode underline = properties.SelectSingleNode("./*[local-name()='u']");
            if (underline != null && Attr(underline, "val") != "none") css.Append("text-decoration:underline;");
            if (IsOn(properties.SelectSingleNode("./*[local-name()='strike']"))) css.Append("text-decoration:line-through;");
            XmlNode size = properties.SelectSingleNode("./*[local-name()='sz']");
            double halfPoints;
            if (size != null && double.TryParse(Attr(size, "val"), NumberStyles.Any, CultureInfo.InvariantCulture, out halfPoints)) css.Append("font-size:").Append((halfPoints / 2.0).ToString("0.###", CultureInfo.InvariantCulture)).Append("pt;");
            XmlNode color = properties.SelectSingleNode("./*[local-name()='color']");
            AppendColor(css, "color", color == null ? "" : Attr(color, "val"));
            XmlNode fonts = properties.SelectSingleNode("./*[local-name()='rFonts']");
            if (fonts != null)
            {
                string font = FirstAttr(fonts, "eastAsia", "ascii", "hAnsi");
                font = Regex.Replace(font ?? "", "['\";<>]", "");
                if (font.Length > 0) css.Append("font-family:'").Append(font).Append("','Microsoft YaHei UI',sans-serif;");
            }
            XmlNode highlight = properties.SelectSingleNode("./*[local-name()='highlight']");
            string highlightColor = HighlightColor(highlight == null ? "" : Attr(highlight, "val"));
            if (highlightColor.Length > 0) css.Append("background-color:").Append(highlightColor).Append(';');
            XmlNode shading = properties.SelectSingleNode("./*[local-name()='shd']");
            AppendColor(css, "background-color", shading == null ? "" : Attr(shading, "fill"));
            XmlNode vertical = properties.SelectSingleNode("./*[local-name()='vertAlign']");
            string verticalValue = vertical == null ? "" : Attr(vertical, "val");
            if (verticalValue == "superscript") css.Append("vertical-align:super;font-size:.78em;");
            else if (verticalValue == "subscript") css.Append("vertical-align:sub;font-size:.78em;");
            return css.ToString();
        }

        private static string BuildCellCss(XmlNode properties)
        {
            if (properties == null) return "";
            StringBuilder css = new StringBuilder();
            XmlNode shading = properties.SelectSingleNode("./*[local-name()='shd']");
            AppendColor(css, "background-color", shading == null ? "" : Attr(shading, "fill"));
            XmlNode vertical = properties.SelectSingleNode("./*[local-name()='vAlign']");
            string value = vertical == null ? "" : Attr(vertical, "val");
            if (value == "center") css.Append("vertical-align:middle;");
            else if (value == "bottom") css.Append("vertical-align:bottom;");
            return css.ToString();
        }

        private static void AppendTwips(StringBuilder css, string property, string value)
        {
            double twips;
            if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out twips)) css.Append(property).Append(':').Append((twips / 20.0).ToString("0.###", CultureInfo.InvariantCulture)).Append("pt;");
        }

        private static void AppendColor(StringBuilder css, string property, string value)
        {
            if (Regex.IsMatch(value ?? "", "^[0-9A-Fa-f]{6}$")) css.Append(property).Append(":#").Append(value).Append(';');
        }

        private static void AppendBorder(StringBuilder css, XmlNode properties, string side)
        {
            XmlNode border = properties.SelectSingleNode("./*[local-name()='pBdr']/*[local-name()='" + side + "']");
            if (border == null) return;
            string kind = Attr(border, "val");
            if (kind == "nil" || kind == "none") return;
            string color = Attr(border, "color");
            if (!Regex.IsMatch(color ?? "", "^[0-9A-Fa-f]{6}$")) color = "C8CED8";
            double eighthPoints;
            if (!double.TryParse(Attr(border, "sz"), NumberStyles.Any, CultureInfo.InvariantCulture, out eighthPoints)) eighthPoints = 8;
            css.Append("border-").Append(side).Append(':').Append(Math.Max(1, eighthPoints / 8.0).ToString("0.###", CultureInfo.InvariantCulture)).Append("pt solid #").Append(color).Append(';');
        }

        private static string HighlightColor(string value)
        {
            switch ((value ?? "").ToLowerInvariant())
            {
                case "yellow": return "#fff59d";
                case "green": return "#b9efc4";
                case "cyan": return "#b8eef4";
                case "magenta": return "#f3b7e8";
                case "blue": return "#b9d6ff";
                case "red": return "#ffc4c4";
                case "darkyellow": return "#e4cf75";
                case "darkgreen": return "#86bf92";
                case "lightgray": return "#e5e7eb";
                case "darkgray": return "#9ca3af";
                default: return "";
            }
        }

        private static bool IsOn(XmlNode node)
        {
            if (node == null) return false;
            string value = Attr(node, "val").ToLowerInvariant();
            return value != "0" && value != "false" && value != "off" && value != "none";
        }

        private static string RepairFragment(string text)
        {
            string value = (text ?? "").Replace("\0", "").Replace("\u000B", "\n");
            if (value.Length == 0 || string.IsNullOrWhiteSpace(value)) return value;
            int start = 0;
            while (start < value.Length && char.IsWhiteSpace(value[start])) start++;
            int end = value.Length;
            while (end > start && char.IsWhiteSpace(value[end - 1])) end--;
            return value.Substring(0, start) + DocumentConverter.RepairWordText(value.Substring(start, end - start)) + value.Substring(end);
        }

        private static string Attr(XmlNode node, string name)
        {
            if (node == null || node.Attributes == null) return "";
            XmlAttribute direct = node.Attributes[name, WordNamespace];
            if (direct != null) return direct.Value;
            foreach (XmlAttribute attribute in node.Attributes) if (attribute.LocalName == name) return attribute.Value;
            return "";
        }

        private static string FirstAttr(XmlNode node, params string[] names)
        {
            foreach (string name in names)
            {
                string value = Attr(node, name);
                if (!string.IsNullOrEmpty(value)) return value;
            }
            return "";
        }

        private static string CssToken(string value)
        {
            return Regex.Replace(value ?? "", "[^A-Za-z0-9_-]", "-");
        }
    }
}
