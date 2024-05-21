using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Opc.UaFx;
using Opc.UaFx.Client;
using Microsoft.Azure.Devices.Client;
using Newtonsoft.Json;
using Microsoft.Azure.Devices.Shared;
using Opc.Ua;

namespace ProductionMonitoringAgent
{
    class Program
    {
        private static List<string> deviceNames = new List<string>();
        private static DeviceClient deviceClient;
        private static DeviceClient deviceClient2;
        private static DeviceClient deviceClient3;
        private static DeviceClient currentdevice;
        private static OpcClient opcClient;
        private static string iotHubConnectionString;

        static async Task Main(string[] args)
        {
            try
            {
                using StreamReader reader = new("connection.txt");
                string text = reader.ReadToEnd();
                iotHubConnectionString = text;
            }
            catch (IOException e)
            {
                Console.WriteLine("The file could not be read:");
                Console.WriteLine(e.Message);
            }

            // OPC UA server configuration
            var opcServerUrl = "opc.tcp://localhost:4840"; // Replace with actual OPC server URL
            opcClient = new OpcClient(opcServerUrl);

            // Azure IoT Hub configuration
            deviceClient = DeviceClient.CreateFromConnectionString("HostName=Zajecia-wmii.azure-devices.net;DeviceId=Device1;SharedAccessKey=sYVN0SZvg2nFo4QAsyg2SbtlQv36CgIO6AIoTHCrIcM=", TransportType.Mqtt);
            deviceClient2 = DeviceClient.CreateFromConnectionString("HostName=Zajecia-wmii.azure-devices.net;DeviceId=Device2;SharedAccessKey=6LLcDkMgbXHETR70MSLaZq5yOwaKgtTPfAIoTNE8Pdo=", TransportType.Mqtt);
            deviceClient3 = DeviceClient.CreateFromConnectionString("HostName=Zajecia-wmii.azure-devices.net;DeviceId=Device3;SharedAccessKey=SkDKYZDKn3iejJgnTLcUqwRP8mx5qm4KLAIoTOCv7v0=", TransportType.Mqtt);
            try
            {
                // Connect to OPC UA server
                opcClient.Connect();
                MethodRequest met = new MethodRequest("EmergencyStop");
                // Browse nodes to get device names
                var nodes = opcClient.BrowseNode(OpcObjectTypes.ObjectsFolder);
                Browse(nodes);
                void Browse(OpcNodeInfo node, int level = 0)
                {
                    if (node.NodeId.Namespace == 2 && level == 1)
                    {
                        deviceNames.Add(node.Attribute(OpcAttribute.DisplayName).Value.ToString());
                    }

                    level++;

                    foreach (var childNode in node.Children())
                    {
                        Browse(childNode, level);
                    }
                }


                await SetupDirectMethodHandlers(deviceClient);
                await SetupDirectMethodHandlers(deviceClient2);
                await SetupDirectMethodHandlers(deviceClient3);


                // Main loop for reading OPC data and sending to IoT Hub
                while (true)
                {
                    foreach (var deviceName in deviceNames)
                    {
                        if (deviceName == "Device 1")
                        {
                            currentdevice = deviceClient;
                        }
                        else if (deviceName == "Device 2")
                        {
                            currentdevice = deviceClient2;
                        }
                        else if (deviceName == "Device 3")
                            currentdevice = deviceClient3;

                        var twin = await currentdevice.GetTwinAsync();

                        var allTelemetryData = new List<object>();
                        var twinUpdate = new TwinCollection();

                        var sanitizedDeviceName = deviceName.Replace(" ", "");
                        var desiredProperties = await currentdevice.GetTwinAsync();
                        var desiredProductionRate = Convert.ToInt32(twin.Properties.Desired[sanitizedDeviceName]?.ProductionRate ?? 0);
                        var currentProductionRate = Convert.ToInt32(opcClient.ReadNode($"ns=2;s={deviceName}/ProductionRate").Value);
                        var nodeId = ($"ns=2;s={deviceName}/ProductionRate");

                        var deviceTwin = await currentdevice.GetTwinAsync();

                        var previousDeviceErrors = Convert.ToInt32(opcClient.ReadNode($"ns=2;s={deviceName}/DeviceErrors").Value);
                        var currentDeviceErrors = Convert.ToInt32(deviceTwin.Properties.Reported[sanitizedDeviceName]?.DeviceErrors ?? 0);

                        if (currentDeviceErrors != previousDeviceErrors)
                        {

                            if (currentDeviceErrors == 14)
                            {
                                opcClient.CallMethod($"ns=2;s={deviceName}", $"ns=2;s={deviceName}/EmergencyStop");
                            }
                            // Create telemetry data for device errors change
                            var ErrorTelemetry = new
                            {
                                deviceName,
                                deviceErrorsChanged = true,
                                previousDeviceErrors,
                                currentDeviceErrors
                            };

                            try
                            {
                                var messageString = JsonConvert.SerializeObject(ErrorTelemetry);
                                var message = new Message(System.Text.Encoding.UTF8.GetBytes(messageString));


                                await currentdevice.SendEventAsync(message);

                                Console.WriteLine($"Sent telemetry data for device errors changes in {deviceName} to IoT Hub");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"An error occurred while sending telemetry data: {ex.Message}");
                                if (ex.InnerException != null)
                                {
                                    Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                                }
                            }
                        }

                        // Read 
                        var productionStatus = opcClient.ReadNode($"ns=2;s={deviceName}/ProductionStatus").Value;
                        var workorderId = opcClient.ReadNode($"ns=2;s={deviceName}/WorkorderId").Value;
                        var productionRate = Convert.ToInt32(opcClient.ReadNode($"ns=2;s={deviceName}/ProductionRate").Value);
                        var goodCount = Convert.ToInt64(opcClient.ReadNode($"ns=2;s={deviceName}/GoodCount").Value);
                        var badCount = Convert.ToInt64(opcClient.ReadNode($"ns=2;s={deviceName}/BadCount").Value);
                        var temperature = Convert.ToDouble(opcClient.ReadNode($"ns=2;s={deviceName}/Temperature").Value);
                        var deviceErrors = Convert.ToInt32(opcClient.ReadNode($"ns=2;s={deviceName}/DeviceError").Value);

                        var totalProduction = goodCount + badCount;
                        var goodProductionRate = totalProduction > 0 ? (double)goodCount / totalProduction * 100 : 100;

                        await currentdevice.SetDesiredPropertyUpdateCallbackAsync(DesiredPropertyUpdateCallback, null);


                        if (goodProductionRate < 90 && productionRate > 0)
                        {
                            //Decrease desired production rate by 10 points
                            var newDesiredProductionRate = productionRate - 10;
                            opcClient.WriteNode($"ns=2;s={deviceName}/ProductionRate", newDesiredProductionRate);
                            Console.WriteLine($"Decreased desired ProductionRate for {deviceName} by 10 points, new value = {newDesiredProductionRate}");

                        }

                        var telemetryData = new
                        {
                            deviceName,
                            productionStatus,
                            workorderId,
                            productionRate,
                            goodCount,
                            badCount,
                            temperature,
                            deviceErrors
                        };
                        allTelemetryData.Add(telemetryData);

                        try
                        {
                            var messageString = JsonConvert.SerializeObject(allTelemetryData);
                            var message = new Message(System.Text.Encoding.UTF8.GetBytes(messageString));


                            await currentdevice.SendEventAsync(message);

                            Console.WriteLine($"Sent telemetry data for {deviceName} to IoT Hub");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"An error occurred while sending telemetry data: {ex.Message}");
                            if (ex.InnerException != null)
                            {
                                Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                            }
                        }
                        // Check if values differ from reported properties
                        var reportedProperties = deviceTwin.Properties.Reported[sanitizedDeviceName];

                        bool updateProductionRate = !reportedProperties.ContainsKey("ProductionRate") || reportedProperties["ProductionRate"].ToObject<int>() != productionRate;
                        bool updateDeviceErrors = !reportedProperties.ContainsKey("DeviceErrors") || reportedProperties["DeviceErrors"].ToObject<int>() != deviceErrors;

                        if (updateProductionRate || updateDeviceErrors)
                        {
                            var deviceTwinUpdate = new TwinCollection();
                            if (updateProductionRate)
                            {
                                deviceTwinUpdate["ProductionRate"] = productionRate;
                            }
                            if (updateDeviceErrors)
                            {
                                deviceTwinUpdate["DeviceErrors"] = deviceErrors;
                            }
                            twinUpdate[sanitizedDeviceName] = deviceTwinUpdate;

                            try
                            {

                                string twinUpdateJson = twinUpdate.ToJson();

                                // Update reported properties
                                await currentdevice.UpdateReportedPropertiesAsync(twinUpdate);

                                var messageString = JsonConvert.SerializeObject(allTelemetryData);
                                var message = new Message(System.Text.Encoding.UTF8.GetBytes(messageString));

                                await currentdevice.SendEventAsync(message);

                                Console.WriteLine($"Updated reported properties for {deviceName}");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"An error occurred while updating reported properties: {ex.Message}");
                                if (ex.InnerException != null)
                                {
                                    Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                                }
                            }
                        }


                    }

                    // Wait for next iteration
                    await Task.Delay(TimeSpan.FromSeconds(10)); // Adjust as needed
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
            }
            finally
            {
                // Disconnect from OPC UA server
                opcClient.Disconnect();
            }
        }

        static async Task SetupDirectMethodHandlers(DeviceClient deviceClient)
        {
            await deviceClient.SetMethodHandlerAsync("EmergencyStop", EmergencyStopHandler, null);
            await deviceClient.SetMethodHandlerAsync("ResetErrorStatus", ResetErrorStatusHandler, null);
        }
        static async Task DesiredPropertyUpdateCallback(TwinCollection desiredProperties, object userContext)
        {
            foreach (var deviceName in deviceNames)
            {
                var sanitizedDeviceName = deviceName.Replace(" ", "");
                var desiredProductionRate = Convert.ToInt32(desiredProperties[sanitizedDeviceName]?.ProductionRate ?? 0);
                var currentProductionRate = Convert.ToInt32(opcClient.ReadNode($"ns=2;s={deviceName}/ProductionRate").Value);

                if (desiredProductionRate != currentProductionRate)
                {
                    // Update production rate only if desired production rate has changed
                    Console.WriteLine($"Updating ProductionRate for {deviceName} to {desiredProductionRate}");
                    opcClient.WriteNode($"ns=2;s={deviceName}/ProductionRate", desiredProductionRate);
                    currentProductionRate = desiredProductionRate;
                }
            }
        }
        static async Task<MethodResponse> EmergencyStopHandler(MethodRequest methodRequest, object userContext)
        {
            try
            {
                string deviceid = methodRequest.DataAsJson.Replace("\"", "");
                Console.WriteLine($"{deviceid}");
                if (methodRequest.DataAsJson != null)
                {
                    if (!string.IsNullOrEmpty(deviceid))
                    {
                        Console.WriteLine($"EmergencyStop method invoked for device: {deviceid}");

                        opcClient.CallMethod($"ns=2;s={deviceid}", $"ns=2;s={deviceid}/EmergencyStop");
                        return new MethodResponse(200);
                    }
                    else
                    {
                        Console.WriteLine("Error: Device name not provided in the payload.");
                        return new MethodResponse(400);
                    }
                }
                else
                {
                    Console.WriteLine("Error: Empty or invalid payload.");
                    return new MethodResponse(400);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while executing EmergencyStop method: {ex.Message}");
                return new MethodResponse(500);
            }
        }

        static async Task<MethodResponse> ResetErrorStatusHandler(MethodRequest methodRequest, object userContext)
        {
            try
            {
                string deviceid = methodRequest.DataAsJson.Replace("\"", "");
                Console.WriteLine($"{deviceid}");

                if (methodRequest.DataAsJson != null)
                {
                    // Pobierz nazwę urządzenia z przekazanego payloadu

                    // Sprawdź, czy istnieje nazwa urządzenia
                    if (!string.IsNullOrEmpty(deviceid))
                    {
                        Console.WriteLine($"ResetErrorStatus method invoked for device: {deviceid}");

                        opcClient.CallMethod($"ns=2;s={deviceid}", $"ns=2;s={deviceid}/ResetErrorStatus");
                        return new MethodResponse(200);
                    }
                    else
                    {
                        // Jeśli nie podano nazwy urządzenia w payloadzie, zwracamy błąd
                        Console.WriteLine("Error: Device name not provided in the payload.");
                        return new MethodResponse(400);
                    }
                }
                else
                {
                    // Jeśli payload jest pusty lub niepoprawny, zwracamy błąd
                    Console.WriteLine("Error: Empty or invalid payload.");
                    return new MethodResponse(400);
                }
            }
            catch (Exception ex)
            {
                // Jeśli wystąpił błąd podczas wywoływania operacji na urządzeniu, zwracamy kod błędu
                Console.WriteLine($"An error occurred while executing ResetErrorStatus method: {ex.Message}");
                return new MethodResponse(500);
            }
        }


    }
}