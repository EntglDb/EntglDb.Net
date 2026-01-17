@echo off
start "Node 1" dotnet run --project samples/EntglDb.Sample.Console/EntglDb.Sample.Console.csproj -- node-1 5001 --localhost --secure
timeout /t 2
start "Node 2" dotnet run --project samples/EntglDb.Sample.Console/EntglDb.Sample.Console.csproj -- node-2 5002 --localhost --secure
