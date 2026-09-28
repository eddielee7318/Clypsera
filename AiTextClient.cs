using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace ClipboardTrail
{
    internal sealed class AiTextClient
    {
        private readonly SettingsData settings;

        public AiTextClient(SettingsData settings) { this.settings = settings; }

        public Task<string> CompleteAsync(string systemPrompt, List<AiMessage> conversation, int maxTokens)
        {
            return Task.Run(delegate { return Complete(systemPrompt, conversation, maxTokens); });
        }

        private string Complete(string systemPrompt, List<AiMessage> conversation, int maxTokens)
        {
            string key = AppData.UnprotectSecret(settings.ApiKeyEncrypted);
            if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("尚未设置 DeepSeek API Key。");
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(settings.DeepSeekEndpoint);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Accept = "application/json";
            request.Headers[HttpRequestHeader.Authorization] = "Bearer " + key;
            request.Timeout = 60000;

            List<object> messages = new List<object>();
            messages.Add(new Dictionary<string, object> { { "role", "system" }, { "content", systemPrompt } });
            foreach (AiMessage message in conversation)
                messages.Add(new Dictionary<string, object> { { "role", message.Role }, { "content", message.Content } });
            Dictionary<string, object> body = new Dictionary<string, object>();
            body["model"] = settings.DeepSeekModel;
            body["temperature"] = 0.2;
            body["max_tokens"] = maxTokens;
            body["messages"] = messages.ToArray();
            byte[] payload = Encoding.UTF8.GetBytes(AppData.CreateSerializer().Serialize(body));
            request.ContentLength = payload.Length;
            using (Stream stream = request.GetRequestStream()) stream.Write(payload, 0, payload.Length);

            string responseText;
            try
            {
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8)) responseText = reader.ReadToEnd();
            }
            catch (WebException ex)
            {
                string detail = ex.Message;
                if (ex.Response != null)
                    using (StreamReader reader = new StreamReader(ex.Response.GetResponseStream(), Encoding.UTF8)) detail = reader.ReadToEnd();
                throw new InvalidOperationException("DeepSeek 请求失败：" + detail);
            }

            Dictionary<string, object> root = AppData.CreateSerializer().DeserializeObject(responseText) as Dictionary<string, object>;
            object[] choices = root == null || !root.ContainsKey("choices") ? null : root["choices"] as object[];
            if (choices == null || choices.Length == 0) throw new InvalidOperationException("DeepSeek 没有返回内容。");
            Dictionary<string, object> choice = choices[0] as Dictionary<string, object>;
            Dictionary<string, object> messageValue = choice == null || !choice.ContainsKey("message") ? null : choice["message"] as Dictionary<string, object>;
            string content = messageValue == null || !messageValue.ContainsKey("content") ? null : Convert.ToString(messageValue["content"]);
            if (string.IsNullOrWhiteSpace(content)) throw new InvalidOperationException("DeepSeek 返回了空内容。");
            return content.Trim();
        }
    }
}
