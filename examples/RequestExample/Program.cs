using Piloterr;

using var client = new PiloterrClient(); // reads PILOTERR_API_KEY
try
{
    var response = await client.RequestAsync("GET", "/v1/usage");
    Console.WriteLine(response.Text);
}
catch (PiloterrApiException exception)
{
    Console.Error.WriteLine($"HTTP {exception.StatusCode}: {exception.Message}");
}
