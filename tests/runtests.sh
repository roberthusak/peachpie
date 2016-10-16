php ./tests/arrays/foreach_001.php

dotnet restore ./Samples/ConsoleApplication1
dotnet build -f .netcoreapp1.0 ./Samples/ConsoleApplication1

cd ./Samples/ConsoleApplication1
dotnet run