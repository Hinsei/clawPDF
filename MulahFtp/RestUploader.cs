using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;
using NLog;

namespace clawSoft.clawPDF.ftpaiman.RestUploader
{
    public class RestUploader
    {
        private string restServer;
        private string restUsername;
        private string restPassword;
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public RestUploader(string server, string username, string password)
        {
            restServer = server;
            restUsername = username;
            restPassword = password;
        }

        public string UploadFile(string localFilePath, string remoteFilePath)
        {
            try
            {
                using (HttpClient client = new HttpClient()) {
                    using (var formData = new MultipartFormDataContent())
                    {
                        var filestream = new FileStream(localFilePath, FileMode.Open, FileAccess.Read);
                        var filecontent = new StreamContent(filestream);

                        filecontent.Headers.Add("Content-Type", "application/octet-stream");

                        formData.Add(filecontent, "receiptFile", Path.GetFileName(localFilePath));

                        try
                        {
                            HttpResponseMessage response = 
                                client.PostAsync("http://printer.simpleloyalty.com/receipts/upload", formData).Result;

                            response.EnsureSuccessStatusCode();

                            string responseBody = response.Content.ReadAsStringAsync().Result;

                            Logger.Error("Response:" + responseBody);

                            return responseBody;
                        }
                        catch (Exception ex) { 
                            Logger.Error("Error:" + ex.Message);
                            return ex.Message;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"RestUploader Error: {ex.Message}");
                return ex.Message;
            }
        }
    }
}
