#r "Newtonsoft.Json"
#r "Microsoft.WindowsAzure.Storage"

#load "../Shared/copyBlobHelpers.csx"

using System;
using System.Net;
using Newtonsoft.Json;
using Microsoft.WindowsAzure.MediaServices.Client;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Auth;

// Read values from the App.config file.
private static readonly string _storageAccountName = Environment.GetEnvironmentVariable("MediaServicesStorageAccountName");
private static readonly string _storageAccountKey = Environment.GetEnvironmentVariable("MediaServicesStorageAccountKey");

// Field for service context.
private static CloudMediaContext _context = null;
private static CloudStorageAccount _destinationStorageAccount = null;


public static async Task<object> Run(HttpRequestMessage req, TraceWriter log)
{
    log.Info($"Webhook was triggered!");

    string jsonContent = await req.Content.ReadAsStringAsync();
    dynamic data = JsonConvert.DeserializeObject(jsonContent);
    log.Info("Request : " + jsonContent);

    // Validate input objects
    int delay = 15000;
    if (data.DestinationContainer == null)
        return req.CreateResponse(HttpStatusCode.BadRequest, new { error = "Please pass DestinationContainer in the input object" });
    if (data.Delay != null)
        delay = data.Delay;
    log.Info("Input - DestinationContainer : " + data.DestinationContainer);
    //log.Info("delay : " + delay);

    log.Info($"Wait " + delay + "(ms)");
    System.Threading.Thread.Sleep(delay);

    CopyStatus copyStatus = CopyStatus.Success;
    try
    {
        string destinationContainerName = data.DestinationContainer;
        CloudBlobContainer destinationBlobContainer = GetCloudBlobContainer(_storageAccountName, _storageAccountKey, destinationContainerName);

        string blobPrefix = null;
        bool useFlatBlobListing = true;
        var destBlobList = destinationBlobContainer.ListBlobs(blobPrefix, useFlatBlobListing, BlobListingDetails.Copy);
        foreach (var dest in destBlobList)
        {
            var destBlob = dest as CloudBlob;
            if (destBlob.CopyState.Status == CopyStatus.Aborted || destBlob.CopyState.Status == CopyStatus.Failed)
            {
                // Log the copy status description for diagnostics and restart copy
                destBlob.StartCopyAsync(destBlob.CopyState.Source);
                copyStatus = CopyStatus.Pending;
            }
            else if (destBlob.CopyState.Status == CopyStatus.Pending)
            {
                // We need to continue waiting for this pending copy
                // However, let us log copy state for diagnostics
                copyStatus = CopyStatus.Pending;
            }
            // else we completed this pending copy
        }
    }
    catch (Exception ex)
    {
        log.Info("Exception " + ex);
        return req.CreateResponse(HttpStatusCode.BadRequest);
    }

    return req.CreateResponse(HttpStatusCode.OK, new
    {
        CopyStatus = copyStatus
    });
}
