# Set the base image as the .NET 8.0 SDK (this includes the runtime)
FROM mcr.microsoft.com/dotnet/sdk:8.0 as build-env

# Copy everything and publish the release (publish implicitly restores and builds)
COPY . ./
RUN dotnet publish ./DotNet.GitHubAction/DotNet.GitHubAction.csproj -c Release -o out --no-self-contained

# Label the container
LABEL maintainer="Eric Hiller<eric@hiller.pro>"
LABEL repository="https://github.com/erichiller/gh-action-cs-metrics"
LABEL homepage="https://github.com/erichiller/gh-action-cs-metrics"

# Label as GitHub action
LABEL com.github.actions.name=".NET code metric analyzer"
LABEL com.github.actions.description="A Github action that maintains a CODE_METRICS.md file, reporting cylcomatic complexity, maintainability index, etc."
LABEL com.github.actions.icon="sliders"
LABEL com.github.actions.color="purple"

# Relayer the .NET SDK, anew with the build output
FROM mcr.microsoft.com/dotnet/sdk:8.0
COPY --from=build-env /out .
ENTRYPOINT [ "dotnet", "/DotNet.GitHubAction.dll" ]
