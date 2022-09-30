using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using System.Text;
using PgpCore;
using Org.BouncyCastle.Bcpg.OpenPgp;

namespace PGP_Decryption_Azure_Function_Docker
{
    public class PGPDecryptBlobTriggerFunction
    {
        [FunctionName("PGPDecryptBlobTriggerFunction")]
        public static async Task RunAsync([BlobTrigger("%pgpdecodesourcecontainername%/{name}.pgp", Connection = "pgpDecodeSourceStorage")]Stream myBlob,
                               Binder binder,
                               string name, 
	                       ILogger log)

        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("pgpPrivateKey")) || 
                string.IsNullOrEmpty(Environment.GetEnvironmentVariable("pgpPhraseSign"))
                ){
                
                log.LogError("Private key or phrase is null or empty.");  
            }else{

                string privateKey = Encoding.UTF8.GetString(Convert.FromBase64String(Environment.GetEnvironmentVariable("pgpPrivateKey")));
                string passPhrase = Environment.GetEnvironmentVariable("pgpPhraseSign");
            
                log.LogInformation($"C# Blob trigger function Processed blob\n Name:{name}.pgp \n Size: {myBlob.Length} Bytes");
         
                try{
                
                    log.LogInformation($"Decrypting file: {name}.pgp using receiver's private key");
                        
                    myBlob.Position = 0;
                    using (Stream decryptedData = await DecryptStreamAsync(myBlob, privateKey, passPhrase))
                    {   
                        log.LogInformation($"Successfully decryped file: {name}.pgp");
                        log.LogInformation($"Moving decrypted file: {name} to target destination");

                        var connectionString = Environment.GetEnvironmentVariable("pgpDecodeTargetStorage");
                        var containerString = Environment.GetEnvironmentVariable("pgpdecodetargetcontainername");

                        var attributes = new Attribute[]
                        {
                            new BlobAttribute(containerString+"/"+name, FileAccess.Write),
                            new StorageAccountAttribute("pgpDecodeTargetStorage")
                        };
                        // we have to use late binding since early binding (in the function decorators) creates an empty file with size 0 even if the function fails.
                        using (var fileOutputStream = await binder.BindAsync<Stream>(attributes))
                        {
                            await decryptedData.CopyToAsync(fileOutputStream);
                        }                 
                        log.LogInformation($"Successfully moved decrypted file: {name} to destination");
                    }                    
                }catch (PgpException pgpException){
                    log.LogError(pgpException.Message);    
                }

            }
        }

        private static async Task<Stream> DecryptStreamAsync(Stream inputStream, string privateKey, string passPhrase){
            
            using (PGP pgp = new PGP())
            {
                Stream memStream = new MemoryStream();
                using (inputStream)
                using (Stream privateKeyStream = GenerateStreamFromString(privateKey))
                {
                    await pgp.DecryptStreamAsync(inputStream, memStream, privateKeyStream, passPhrase);
                    memStream.Seek(0, SeekOrigin.Begin);
                    return memStream;
                }
            }
        }

        private static Stream GenerateStreamFromString(string s){
            
            var stream = new MemoryStream();
            var writer = new StreamWriter(stream);
            writer.Write(s);
            writer.Flush();
            stream.Position = 0;
            return stream;
        }

    }
}                            