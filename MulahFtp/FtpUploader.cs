using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Net;

namespace clawSoft.clawPDF.ftpaiman.FtpUploader
{
    public class FtpUploader
    {
        private string ftpServer;
        private string ftpUsername;
        private string ftpPassword;

        public FtpUploader(string server, string username, string password)
        {
            ftpServer = server;
            ftpUsername = username;
            ftpPassword = password;
        }

        public bool UploadFile(string localFilePath, string remoteFilePath)
        {
            try
            {
                FileInfo fileInfo = new FileInfo(localFilePath);
                string uri = $"ftp://{ftpServer}/{remoteFilePath}";

                FtpWebRequest request = (FtpWebRequest)WebRequest.Create(uri);
                request.Method = WebRequestMethods.Ftp.UploadFile;
                request.Credentials = new NetworkCredential(ftpUsername, ftpPassword);
                request.UseBinary = true;
                request.ContentLength = fileInfo.Length;

                byte[] buffer = new byte[4096];
                int bytesRead = 0;

                using (FileStream fs = fileInfo.OpenRead())
                using (Stream requestStream = request.GetRequestStream())
                {
                    while ((bytesRead = fs.Read(buffer, 0, buffer.Length)) != 0)
                    {
                        requestStream.Write(buffer, 0, bytesRead);
                    }
                }

                using (FtpWebResponse response = (FtpWebResponse)request.GetResponse())
                {
                    Console.WriteLine($"Upload File Complete, status {response.StatusDescription}");
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                return false;
            }
        }
    }
}
