using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Opc.UaFx;
using Opc.UaFx.Client;
using Microsoft.Azure.Devices.Client;
using Newtonsoft.Json;
using Microsoft.Azure.Devices.Shared;
using Opc.Ua;
using System.Reflection;
using System.Reflection;
namespace ProductionMonitoringAgent
{
    class Program
    {
        private static List<string> deviceNames = new List<string>();
        private static List<DeviceClient> deviceClients = new List<DeviceClient>();
        //private static DeviceClient deviceClient;
        private static DeviceClient deviceClient2;
        private static DeviceClient deviceClient3;
        private static DeviceClient currentdevice;
        private static OpcClient opcClient;
        private static string iotHubConnectionString;

        static async Task Main(string[] args)
        {

            // OPC UA server configuration
            var opcServerUrl = "opc.tcp://localhost:4840"; // Replace with actual OPC server URL
            opcClient = new OpcClient(opcServerUrl);

            // Azure IoT Hub configuration             
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

                while (true)
                {
                    Console.WriteLine("Wprowadz connectionstring, lub wprowadz 0 w celu zakonczenia wprowadzania");
                    string input = Console.ReadLine();
                    if (input == "0")
                    {
                        Console.Clear();
                        break; // Opuszczenie funkcji
                    }
                    else
                    {

                        deviceClients.Add(DeviceClient.CreateFromConnectionString(input, TransportType.Mqtt));
                    }


                }



                foreach (DeviceClient deviceClient in deviceClients)
                {
                    await SetupDirectMethodHandlers(deviceClient);
                }
                int deviceindex;

                // Main loop for reading OPC data and sending to IoT Hub
                while (true)
                {
                    foreach (var deviceName in deviceNames)
                    {
                        deviceindex = Convert.ToInt32(deviceName.Split(' ')[1]);
                        currentdevice = deviceClients[deviceindex - 1];
                      




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

                            //Console.WriteLine($"Sent telemetry data for {deviceName} to IoT Hub");
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
            await deviceClient.SetMethodHandlerAsync("EmergencyStop", EmergencyStopHandler, deviceClient);
            await deviceClient.SetMethodHandlerAsync("ResetErrorStatus", ResetErrorStatusHandler, deviceClient);
        }
        static async Task DesiredPropertyUpdateCallback(TwinCollection desiredProperties, object userContext)
        {
            foreach (var deviceName in deviceNames)
            {
                var sanitizedDeviceName = deviceName.Replace(" ", "");
                if (desiredProperties.Contains(sanitizedDeviceName))
                {
                    var desiredProductionRate = Convert.ToInt32(desiredProperties[sanitizedDeviceName]["ProductionRate"] ?? 0);
                    var currentProductionRate = Convert.ToInt32(opcClient.ReadNode($"ns=2;s={deviceName}/ProductionRate").Value);

                    if (desiredProductionRate != currentProductionRate)
                    {
                        // Update production rate only if desired production rate has changed
                        Console.WriteLine($"Updating ProductionRate for {deviceName} to {desiredProductionRate}");
                        opcClient.WriteNode($"ns=2;s={deviceName}/ProductionRate", desiredProductionRate);
                    }
                }
            }
        }
        static async Task<MethodResponse> EmergencyStopHandler(MethodRequest methodRequest, object userContext)
        {
            var deviceClient = (DeviceClient)userContext; // Retrieve the DeviceClient instance from user context
            try
            {
                string deviceId = methodRequest.DataAsJson.Replace("\"", "");
                Console.WriteLine($"{deviceId}");
                if (!string.IsNullOrEmpty(deviceId))
                {
                    Console.WriteLine($"EmergencyStop method invoked for device: {deviceId}");

                    // Call the OPC method on the device
                    opcClient.CallMethod($"ns=2;s={deviceId}", $"ns=2;s={deviceId}/EmergencyStop");
                    return new MethodResponse(200);
                }
                else
                {
                    Console.WriteLine("Error: Device name not provided in the payload.");
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
            var deviceClient = (DeviceClient)userContext; // Retrieve the DeviceClient instance from user context
            try
            {
                string deviceId = methodRequest.DataAsJson.Replace("\"", "");
                Console.WriteLine($"{deviceId}");

                if (!string.IsNullOrEmpty(deviceId))
                {
                    Console.WriteLine($"ResetErrorStatus method invoked for device: {deviceId}");

                    // Call the OPC method on the device
                    opcClient.CallMethod($"ns=2;s={deviceId}", $"ns=2;s={deviceId}/ResetErrorStatus");
                    return new MethodResponse(200);
                }
                else
                {
                    Console.WriteLine("Error: Device name not provided in the payload.");
                    return new MethodResponse(400);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while executing ResetErrorStatus method: {ex.Message}");
                return new MethodResponse(500);
            }
        }
    }
}