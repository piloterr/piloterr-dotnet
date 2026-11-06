using Piloterr;

using var client = new PiloterrClient(); // reads PILOTERR_API_KEY
var submitted = await client.AmazonProductAsync(new AmazonProductParams { Query = "B09DFCB66S", IsAsync = true });
var job = await client.WaitForJobAsync(
    submitted.Json.GetProperty("job_id").GetString()!,
    onProgress: j => Console.WriteLine($"{j.GetProperty("status")} {j.GetProperty("progress")}%"));
Console.WriteLine(job.Json);
