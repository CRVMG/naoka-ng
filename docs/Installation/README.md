### Installation documentation for Naoka
The following document(s) will be covering the pre-requisites & installation procedure for Naoka, the Photon plugin of the server emulator.

Remember that the server emulator is composed of two parts, [Shoya](https://gitlab.com/george/shoya-go) (API) & [Naoka](https://gitlab.com/george/naoka-ng) (Photon Server plugin). Both of them are required for a fully-playable instance.

---

#### Pre-requisites
 * A working Shoya installation (see [Shoya installation](https://gitlab.com/george/shoya-go/blob/master/docs/README.md))
 * A copy of the Photon Server Plugin SDK (see [Photon Server Plugin SDK](https://www.photonengine.com/en-US/sdks#server-sdkserverserverplugin))
 * A licensed copy of Photon Server v5. The download for which is available on [Exit Games' website](https://www.photonengine.com/en-US/sdks#server-sdkserverserver).
   * Notice: The use & operation of Photon Server is subject to Exit Games' license agreement(s), which can be found [on their website](https://photonengine.com).
   * Exit Games kindly provides a free 100 concurrent user license for Photon Server on [their website](https://dashboard.photonengine.com/en-US/SelfHosted).

#### Step 0 - Compiling Naoka
Due to license restrictions, the Photon Server plugin is not available for download as a pre-compiled binary. Therefore, you will have to compile it yourself.
 * Download the Photon Server Plugin SDK from [here](https://www.photonengine.com/en-US/sdks#server-sdkserverserverplugin)
 * Extract the SDK to a folder
 * Open the Naoka project in Visual Studio or Rider
 * Properly configure the assembly reference for `PhotonHivePlugin.dll`.
 * Build the project

#### Step 1 - Installing the plugin
After compiling, take the compiled `NaokaGo.dll` & `Newtonsoft.Json.dll` and copy them to the `Plugins\NaokaGo\bin` folder of the Photon Server.

In `LoadBalancing\GameServer\bin`, open `plugin.config` & add aa `Plugin` entry for Naoka:
```xml
<Plugin Name="NaokaGo"
    AssemblyName="NaokaGo.dll"
    Type="NaokaGo.NaokaGoFactory"
    ApiUrl="The URL of your Shoya install. For example: http://localhost:8080"
    PhotonApiSecret="The secret you set in Shoya." />
```

#### Step 2 - Configuring the NameServer
In `deploy/Nameserver.json`, you will have to add entries for the following list of regions:
  - `us`
  - `us/*`
  - `usw`
  - `usw/*`
  - `eu`
  - `eu/*`
  - `jp`
  - `jp/*`

Additionally, you will have to modify `deploy/NameServer/bin/Nameserver.xml.config` to add the Custom Authentication provider; An example configuration is provided below.
```xml
    <CustomAuth Enabled="true" AllowAnonymous="false">

      <!-- Custom Authentication Queue Settings -->
      <HttpQueueSettings>
        <MaxConcurrentRequests>50</MaxConcurrentRequests>
        <MaxQueuedRequests>5000</MaxQueuedRequests>
        <MaxErrorRequests>100</MaxErrorRequests>
        <MaxTimedOutRequests>10</MaxTimedOutRequests>
        <HttpRequestTimeout>30000</HttpRequestTimeout>
        <ReconnectInterval>60000</ReconnectInterval>
        <QueueTimeout>20000</QueueTimeout>
        <MaxBackoffTime>10000</MaxBackoffTime>
        <LimitHttpResponseMaxSize>20000</LimitHttpResponseMaxSize>
      </HttpQueueSettings>

      <UseCustomAuthService>true</UseCustomAuthService>

      <AuthProviders>
        <AuthProvider Name="Custom"
                      AuthenticationType="0"
                      AuthUrl="https://{SHOYA_INSTALL_URL}/api/1/photon/ns"
                      secret="{SHOYA_PHOTON_SECRET}" />
      </AuthProviders>
    </CustomAuth>
```

