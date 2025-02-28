#!/bin/sh

dotnet tool uninstall -g FlightDeck
dotnet pack -c Release -o nupkg
dotnet tool install --add-source ./nupkg -g flightdeck
echo "Finished flightdeck reset"