properties {
	$pwd = Split-Path $psake.build_script_file	
	$build_directory  = "$pwd\output\condep-loadbalance-aloha-haproxy"
	$configuration = "Release"
	$preString = "-beta"
	$releaseNotes = ""
	$nunitPath = "$pwd\..\src\packages\NUnit.Runners.2.6.3\tools"
	$nuget = "$pwd\..\tools\nuget.exe"
}
 
include .\..\tools\psake_ext.ps1

function GetNugetAssemblyVersion($assemblyPath) {
    
    if(Test-Path Env:\APPVEYOR_BUILD_VERSION)
    {
        #When building on appveyor, set correct build number.
        $appVeyorBuildVersion = $env:APPVEYOR_BUILD_VERSION
        
        $betaString = "."
        if($appVeyorBuildVersion.Contains("beta"))
        {
        	$betaString = "-beta"
        }	

        $version = $appVeyorBuildVersion.Split('-') | Select-Object -First 1
        $buildNumber = $appVeyorBuildVersion.Split('-') | Select-Object -Last 1 | % {$_.replace("beta","")}

        switch ($buildNumber.length) 
        { 
            1 {$buildNumber = $buildNumber.Insert(0, '0').Insert(0, '0').Insert(0, '0').Insert(0, '0')} 
            2 {$buildNumber = $buildNumber.Insert(0, '0').Insert(0, '0').Insert(0, '0')} 
            3 {$buildNumber = $buildNumber.Insert(0, '0').Insert(0, '0')}
            4 {$buildNumber = $buildNumber.Insert(0, '0')}                
            default {$buildNumber = $buildNumber}
        }

        return "$version$betaString$buildNumber"
    }
    else
    {
        $versionInfo = Get-Item $assemblyPath | % versioninfo
        return "$($versionInfo.FileVersion)"
    }
}

task default -depends Build-All, Pack-All
task ci -depends Build-All, Pack-All

task Build-All -depends Clean, RestoreNugetPackages, Build, Create-BuildSpec-Aloha-HaProxy
task Pack-All -depends Pack-Aloha-HaProxy

task RestoreNugetPackages {
	Exec { & $nuget restore "$pwd\..\src\condep-loadbalance-aloha-haproxy.sln" }
}
task Build {
	Exec { msbuild "$pwd\..\src\condep-loadbalance-aloha-haproxy.sln" /t:Build /p:Configuration=$configuration /p:OutDir=$build_directory /p:GenerateProjectSpecificOutputFolder=true}
}

task Clean {
	Write-Host "Cleaning Build output"  -ForegroundColor Green
	Remove-Item $build_directory -Force -Recurse -ErrorAction SilentlyContinue
}

task Create-BuildSpec-Aloha-HaProxy {
	Generate-Nuspec-File `
		-file "$build_directory\condep.dsl.loadbalancer.alohahaproxy.nuspec" `
		-version $(GetNugetAssemblyVersion $build_directory\ConDep.Dsl.LoadBalancer.AlohaHaProxy\ConDep.Dsl.LoadBalancer.AlohaHaProxy.dll) `
		-id "ConDep.Dsl.LoadBalancer.AlohaHaProxy" `
		-title "ConDep.Dsl.LoadBalancer.AlohaHaProxy" `
		-licenseUrl "http://www.condep.io/license/" `
		-projectUrl "http://www.condep.io/" `
		-description "ConDep integration with the ALOHA HAProxy load balancer" `
		-iconUrl "https://raw.github.com/condep/ConDep/master/images/ConDepNugetLogo.png" `
		-releaseNotes "$releaseNotes" `
		-tags "ALOHA HAPROXY loadbalance Continuous Deployment Delivery Infrastructure WebDeploy Deploy msdeploy IIS automation powershell remote" `
		-dependencies @(
			@{ Name="ConDep.Dsl"; Version="3.1.0"},
			@{ Name="SnmpSharpNet"; Version="0.9.4"}
		) `
		-files @(
			@{ Path="ConDep.Dsl.LoadBalancer.AlohaHaProxy\ConDep.Dsl.LoadBalancer.AlohaHaProxy.dll"; Target="lib/net40"}
		)
}

task Pack-Aloha-HaProxy {
	Exec { & $nuget pack "$build_directory\condep.dsl.loadbalancer.alohahaproxy.nuspec" -OutputDirectory "$build_directory" }
}