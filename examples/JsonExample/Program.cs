using Piloterr;

using var client = new PiloterrClient(); // reads PILOTERR_API_KEY
var response = await client.AmazonProductAsync(new AmazonProductParams { Query = "B09DFCB66S" });
Console.WriteLine(response.Json);
