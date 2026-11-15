# Reproduce a Foundry request in C#

Prerequisites are the .NET 10 SDK, Azure CLI signed in to an identity with inference access to your deployment, a compatible GPT-5-mini deployment and its HTTPS OpenAI v1 base URL. Use your resource's `/openai/v1/` base and your deployment alias.

Check the body and endpoint without a network call from Module 06.

```powershell
dotnet run --project hosted-sender -- check $base $deployment hosted-requests/m06-1-baseline.json unused
```

With access to your own authorized deployment, send one reviewed request and use a new output directory.

```powershell
az account get-access-token --resource https://cognitiveservices.azure.com --query accessToken -o tsv |
  dotnet run --project hosted-sender -- send $base $deployment hosted-requests/m06-1-baseline.json runs/6-1-baseline-01
```

Edit the instruction in a copy of the request file. Keep the same user message, deployment, reasoning setting and token cap. Run it with a new output directory, then compare response.json and receipt.json. The sender replaces the body's model value with your deployment alias. It retains the other reviewed request fields, disables HTTP redirects, makes one application request, and never writes the bearer token. The recorded comparison in the video came from the instructor’s separate HTTPS recorder. The C# replay project independently checks those recordings and makes no network requests.


