php ./tests/arrays/foreach_001.php

dotnet restore ./Samples/ConsoleApplication1

# Diagnose the presence of various compilation and packaging outputs in the filesystem
find ./src
find ~/.nuget/packages/.tools
find ~/.nuget/packages/Peachpie.Compiler.Tools
cat  ~/.nuget/packages/Peachpie.Compiler.Tools/0.2.0-beta/lib/netcoreapp1.0/dotnet-compile-php.runtimeconfig.json

dotnet build -f .netcoreapp1.0 ./Samples/ConsoleApplication1

cd ./Samples/ConsoleApplication1
dotnet run