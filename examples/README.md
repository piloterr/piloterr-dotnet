# Examples

```sh
export PILOTERR_API_KEY="YOUR_API_KEY"
dotnet run --project examples/JsonExample
```

- `JsonExample/` — call the featured endpoint and print the JSON result.
- `BinaryExample/` — save a binary response to a file.
- `RequestExample/` — call any path with `RequestAsync` and handle API errors.
- `WaitForJobExample/` — start an async job and wait for its result.
