using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Opc.UaFx;
using Opc.UaFx.Client;
using Microsoft.Azure.Devices.Client;
using Newtonsoft.Json;
using Microsoft.Azure.Devices.Shared;

namespace ProductionMonitoringAgent
{
    class Program
    {
        private static List<string> deviceNames = new List<string>();

        static async Task Main(string[] args)
        {
            // OPC UA server configuration
            var opcServerUrl = "opc.tcp://localhost:4840"; // Replace with actual OPC server URL
            var opcClient = new OpcClient(opcServerUrl);

            // Azure IoT Hub configuration
            var iotHubConnectionString = "HostName=Zajecia-wmii.azure-devices.net;DeviceId=Device1;SharedAccessKey=BsFRd2Vy4SWlmqLLkpX8fqf54rD+xiUrNAIoTA2IEJc="; // Replace with actual IoT Hub connection string
            var deviceClient = DeviceClient.CreateFromConnectionString(iotHubConnectionString, TransportType.Mqtt);

            try
            {
                // Connect to OPC UA server
                opcClient.Connect();

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

                // Main loop for reading OPC data and sending to IoT Hub
                while (true)
                {
                    var allTelemetryData = new List<object>();
                    var twinUpdate = new TwinCollection();

                    foreach (var deviceName in deviceNames)
                    {
                        var productionStatus = opcClient.ReadNode($"ns=2;s={deviceName}/ProductionStatus").Value;
                        var workorderId = opcClient.ReadNode($"ns=2;s={deviceName}/WorkorderId").Value;
                        var productionRate = Convert.ToDouble(opcClient.ReadNode($"ns=2;s={deviceName}/ProductionRate").Value);
                        var goodCount = Convert.ToInt64(opcClient.ReadNode($"ns=2;s={deviceName}/GoodCount").Value);
                        var badCount = Convert.ToInt64(opcClient.ReadNode($"ns=2;s={deviceName}/BadCount").Value);
                        var temperature = Convert.ToDouble(opcClient.ReadNode($"ns=2;s={deviceName}/Temperature").Value);
                        var deviceErrors = Convert.ToInt32(opcClient.ReadNode($"ns=2;s={deviceName}/DeviceErrors").Value);

                        // Debugging: Print the read values
                        Console.WriteLine($"Device: {deviceName}, ProductionStatus: {productionStatus}, WorkorderId: {workorderId}, ProductionRate: {productionRate}, GoodCount: {goodCount}, BadCount: {badCount}, Temperature: {temperature}, DeviceErrors: {deviceErrors}");

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

                        // Update reported properties for this device
                        var sanitizedDeviceName = deviceName.Replace(" ", "");
                        var deviceTwinUpdate = new TwinCollection
                        {
                            ["ProductionRate"] = productionRate,
                            ["DeviceErrors"] = deviceErrors
                        };

                        twinUpdate[sanitizedDeviceName] = deviceTwinUpdate;
                    }

                    try
                    {
                        // Debug output of the twinUpdate JSON
                        string twinUpdateJson = twinUpdate.ToJson();
                        Console.WriteLine("Twin Update JSON: " + twinUpdateJson);

                        // Update reported properties
                        await deviceClient.UpdateReportedPropertiesAsync(twinUpdate);

                        var messageString = JsonConvert.SerializeObject(allTelemetryData);
                        var message = new Message(System.Text.Encoding.UTF8.GetBytes(messageString));

                        // Send telemetry message to IoT Hub
                        await deviceClient.SendEventAsync(message);

                        Console.WriteLine("Updated reported properties for all devices");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"An error occurred while updating reported properties: {ex.Message}");
                        if (ex.InnerException != null)
                        {
                            Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
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
    }
}
