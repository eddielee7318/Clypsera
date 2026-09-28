using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace ClipboardTrail
{
    internal sealed class DeepSeekClient
    {
        private readonly SettingsData settings;

        public DeepSeekClient(SettingsData settings)
        {
            this.settings = settings;
        }

        public Task<string> ClassifyAsync(string text)
        {
            return Task.Run(delegate { return Classify(text); });
        }

        public Task<AiRouteResult> ClassifyAndRouteAsync(string text, List<DocumentChoice> documents)
        {
            return Task.Run(delegate { return ClassifyAndRoute(text, documents); });
        }

        private string Classify(string text)
        {
            return ClassifyAndRoute(text, new List<DocumentChoice>()).Category;
        }

        private AiRouteResult ClassifyAndRoute(string text, List<DocumentChoice> documents)
        {
            string key = AppData.UnprotectSecret(settings.ApiKeyEncrypted);
            if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("尚未设置 DeepSeek API Key。");
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(settings.DeepSeekEndpoint);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Accept = "application/json";
            request.Headers[HttpRequestHeader.Authorization] = "Bearer " + key;
            request.Timeout = 30000;

            string clipped = text == null ? "" : text;
            if (clipped.Length > 4000) clipped = clipped.Substring(0, 4000);
            StringBuilder systemPrompt = new StringBuilder();
            systemPrompt.Append("你是剪贴板资料整理助手。第一项返回一个2到8个汉字的中文主题分类；第二项从用户已有文档中选择最适合的归档编号，没有合适文档就返回NONE。只返回：分类|编号，不要解释、引号或其他文字。可参考分类：链接、代码、工作、学习、灵感、待办、地址、账号、长文摘录、普通文本。\n现有文档：\n");
            Dictionary<string, string> tokenToId = new Dictionary<string, string>();
            int index = 1;
            foreach (DocumentChoice document in (documents ?? new List<DocumentChoice>()).Take(120))
            {
                string token = "D" + index;
                tokenToId[token] = document.Id;
                systemPrompt.Append(token).Append(" = ").Append(document.Path).Append("\n");
                index++;
            }
            if (tokenToId.Count == 0) systemPrompt.Append("（暂无文档，请返回NONE）");

            Dictionary<string, object> body = new Dictionary<string, object>();
            body["model"] = settings.DeepSeekModel;
            body["temperature"] = 0;
            body["max_tokens"] = 30;
            body["messages"] = new object[]
            {
                new Dictionary<string, object> { { "role", "system" }, { "content", systemPrompt.ToString() } },
                new Dictionary<string, object> { { "role", "user" }, { "content", clipped } }
            };
            byte[] payload = Encoding.UTF8.GetBytes(AppData.CreateSerializer().Serialize(body));
            request.ContentLength = payload.Length;
            using (Stream stream = request.GetRequestStream()) stream.Write(payload, 0, payload.Length);
            string responseText;
            try
            {
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                    responseText = reader.ReadToEnd();
            }
            catch (WebException ex)
            {
                string detail = ex.Message;
                if (ex.Response != null)
                {
                    using (StreamReader reader = new StreamReader(ex.Response.GetResponseStream(), Encoding.UTF8))
                        detail = reader.ReadToEnd();
                }
                throw new InvalidOperationException("DeepSeek 请求失败：" + detail);
            }

            Dictionary<string, object> root = AppData.CreateSerializer().DeserializeObject(responseText) as Dictionary<string, object>;
            if (root == null || !root.ContainsKey("choices")) throw new InvalidOperationException("DeepSeek 返回格式无法识别。");
            object[] choices = root["choices"] as object[];
            if (choices == null || choices.Length == 0) throw new InvalidOperationException("DeepSeek 没有返回分类。");
            Dictionary<string, object> choice = choices[0] as Dictionary<string, object>;
            Dictionary<string, object> message = choice == null ? null : choice["message"] as Dictionary<string, object>;
            string raw = message == null ? null : Convert.ToString(message["content"]);
            string[] resultParts = (raw ?? "").Trim().Split('|');
            string category = Sanitize(resultParts.Length > 0 ? resultParts[0] : raw);
            if (string.IsNullOrWhiteSpace(category)) throw new InvalidOperationException("DeepSeek 返回了空分类。");
            string documentId = null;
            if (resultParts.Length > 1)
            {
                string token = resultParts[1].Trim().ToUpperInvariant().Trim('"', '\'', '`', '。');
                if (tokenToId.ContainsKey(token)) documentId = tokenToId[token];
            }
            return new AiRouteResult { Category = category, DocumentId = documentId };
        }

        private static string Sanitize(string value)
        {
            if (value == null) return "";
            value = value.Trim().Trim('"', '\'', '`', '。', '，', ':', '：');
            int newline = value.IndexOfAny(new char[] { '\r', '\n' });
            if (newline >= 0) value = value.Substring(0, newline).Trim();
            if (value.Length > 20) value = value.Substring(0, 20);
            return value;
        }
    }
}
