#tool "nuget:?package=NUnit.Runners&version=2.6.4"
#addin "Cake.Powershell"
#addin "NuGet.Core"
#addin "Cake.ExtendedNuGet"
#addin "Cake.FileHelpers"

var projectName = "condep-loadbalance-aloha-haproxy";
var projectIdentifier = "ConDep.Dsl.LoadBalancer.AlohaHaProxy";
var pwd = MakeAbsolute(Directory("."));
var toolsDir = pwd + Directory("/.cake");
var sourceDirectory = MakeAbsolute(Directory(".."));
var buildOutputDirectory = pwd + Directory("/output");
var solutionFile = sourceDirectory + "/src/" + projectName + ".sln";
var target = Argument("target", "Default");
var configuration = Argument("configuration", "Debug");

Task("default").IsDependentOn("clean").IsDependentOn("restore-packages").IsDependentOn("build").IsDependentOn("version-exists-check").IsDependentOn("pack-nuget-file");

Task("clean").Does(() =>
{
	if(DirectoryExists(buildOutputDirectory))
	{
		var deleteDirectorySettings = new DeleteDirectorySettings {
			Recursive = true,
			Force = true
		};
		DeleteDirectory(buildOutputDirectory, deleteDirectorySettings);
	}
});

Task("restore-packages").Does(() =>
{
    NuGetRestore(solutionFile);
});

Task("build").Does(() =>
{
	var args = $"/t:Build /p:Configuration={configuration} /p:OutDir={buildOutputDirectory} /p:GenerateProjectSpecificOutputFolder=true";
    MSBuild(solutionFile, new MSBuildSettings { ArgumentCustomization = a => a.Append(args) });
});

Task("version-exists-check").Does(() =>
{
	var version = GetAssemblyVersion("../src/"+projectIdentifier+"/Properties/AssemblyInfo.cs");
	var versionExists = IsNuGetPublished(projectIdentifier, version, "https://www.myget.org/F/condep/api/v2");

	if(versionExists)
	{
		throw new System.Exception(projectIdentifier + " " + version + " already exists on myget. Have you forgot to update version in appveyor.yml?");
	}
});

Task("pack-nuget-file").Does(() =>
{
	var version = GetAssemblyVersion("../src/"+projectIdentifier+"/Properties/AssemblyInfo.cs");
	var nuGetPackSettings   = new NuGetPackSettings {
        Id                      = projectIdentifier,
        Version                 = version,
        Title                   = projectIdentifier,
        Authors                 = new[] {"Jon Arild Torresdal"},
        Owners                  = new[] {"Jon Arild Torresdal"},
        Description             = "LoadBalancer api provider for HAProxy.",
        Summary                 = "LoadBalancer api provider for HAProxy.",
        ProjectUrl              = new Uri("http://www.condep.io/"),
        IconUrl                 = new Uri("https://raw.github.com/condep/ConDep/master/images/ConDepNugetLogo.png"),
        LicenseUrl              = new Uri("http://www.condep.io/license/"),
        Copyright               = "Copyright ConDep 2017",
        ReleaseNotes            = new [] {""},
        Tags                    = new [] {"Continuous", "Deployment", "Delivery", "Infrastructure", "WebDeploy", "Deploy", "msdeploy", "IIS", "automation", "powershell", "remote", "haproxy", "aloha"},
        RequireLicenseAcceptance= false,
        Symbols                 = false,
        NoPackageAnalysis       = true,
        Files                   = new [] {
                                            new NuSpecContent {Source = buildOutputDirectory + "/"+projectIdentifier+"/"+projectIdentifier+".dll", Target = "lib/net45"},
                                        },
        BasePath                = buildOutputDirectory + "/" + projectIdentifier,
        OutputDirectory         = buildOutputDirectory
	};

	NuGetPack(nuGetPackSettings);
});

RunTarget(target);

private string GetAssemblyVersion(string assemblyFilePath)
{
	var appVeyorBuildVersion = EnvironmentVariable("APPVEYOR_BUILD_VERSION");

	if(appVeyorBuildVersion != null)
	{
		//Getting the version number. Without the beta part, if its a beta package   
		var version = appVeyorBuildVersion.Split('.');
		var major = version[0]; 
		var minor = version[1]; 
		var patch = version[2].Split('-').First();

		//Setting beta postfix, if beta build. The beta number must be 5 digits, therefor this operation.
		var betaString = "";
		if(appVeyorBuildVersion.Contains("beta"))
		{
			var buildNumber = appVeyorBuildVersion.Split('-').Last().Replace("beta", "");
			switch (buildNumber.Length) 
			{
				case 1:
					buildNumber = "0000" + buildNumber;
					break;
				case 2:
					buildNumber = "000" + buildNumber;
					break;
				case 3:
					buildNumber = "00" + buildNumber;
					break;
				case 4:
					buildNumber = "0" + buildNumber;
					break;
				default:
					buildNumber = buildNumber;
					break;
			}
			betaString = "-beta" + buildNumber; 
		}	
		return major + "." + minor + "." + patch + betaString;
	}
	else
	{
		//When building on local machine, set versionnumber from assembly info.
		var assemblyInfo = ParseAssemblyInfo(assemblyFilePath);
		return assemblyInfo.AssemblyFileVersion;
	}
}