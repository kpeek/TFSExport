using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Configuration;
using Newtonsoft.Json.Linq;
using System.Linq;
using CsvHelper;
using Microsoft.Extensions.Configuration;

namespace QueryTFS
{
    class Program
    {
        static HttpClient httpClient = new HttpClient();
        static string baseUrl; // "https://raulandborg-dryrun.visualstudio.com";
        static string personalaccesstoken; // "zziofwmllhmjurtk3hsuescdw2iszj5ihvctnxzajviupqdbbawq";

        static void Main(string[] args)
        {
            try
            {

                IConfiguration config = new ConfigurationBuilder()
                    .AddJsonFile("appSettings.json", true, true)
                    .Build();

                baseUrl = config["BaseUrl"];
                personalaccesstoken = config["PAT"];

                httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                var base64String = Convert.ToBase64String(Encoding.ASCII.GetBytes(string.Format("{0}:{1}", "", personalaccesstoken)));
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", base64String);

                var agentPools = GetAgentPools();

                var projects = GetProjects();

                var builds = GetBuilds(projects);

                var agentQueues = GetAgentQueues(projects);

                // fill in agent queue properties
                foreach (var agentQueue in agentQueues)
                {
                    var matchingPool = agentPools.FirstOrDefault(x => x.Id == agentQueue.PoolId);
                    if (matchingPool != null)
                    {
                        agentQueue.AgentPool = matchingPool;
                    }

                    var matchingProject = projects.FirstOrDefault(x => x.Id == agentQueue.ProjectId);
                    if (matchingProject != null)
                    {
                        agentQueue.Project = matchingProject;
                    }
                }


                // fill in build properties
                foreach (var build in builds)
                {
                    var matchingQueue = agentQueues.FirstOrDefault(x => x.Id == build.QueueId && x.ProjectId == build.Project.Id);
                    if (matchingQueue != null)
                    {
                        build.Queue = matchingQueue;
                    }
                }

                // Write CSVs

                // Builds
                using(var writer = System.IO.File.CreateText("Builds.csv"))
                {
                    var csv = new CsvWriter(writer);

                    csv.WriteField("Id");
                    csv.WriteField("Name");
                    csv.WriteField("Path");
                    csv.WriteField("Project");
                    csv.WriteField("Queue");
                    csv.WriteField("Pool");
                    csv.WriteField("Repository");
                    csv.WriteField("DefaultBranch");
                    csv.NextRecord();

                    foreach (var build in builds)
                    {
                        csv.WriteField(build.Id);
                        csv.WriteField(build.Name);
                        csv.WriteField(build.Path);
                        csv.WriteField(build.Project.Name);
                        csv.WriteField(build.Queue?.Name);
                        csv.WriteField(build.Queue?.AgentPool?.Name);
                        csv.WriteField(build.RepositoryName);
                        csv.WriteField(build.DefaultBranch);
                        csv.NextRecord();
                    }

                    csv.Flush();
                }

                // Agents
                using (var writer = System.IO.File.CreateText("Agents.csv"))
                {
                    var csv = new CsvWriter(writer);

                    /*
                     * "id": 4,
                        "name": "R5EBUILDAGENT02",
                        "version": "2.134.2",
                        "osDescription": "Microsoft Windows 10.0.14393 ",
                        "enabled": true,
                        "status": "offline",

                        "Agent.Name": "vststestAgt1",
                        "Agent.Version": "2.134.2",
                        "Agent.ComputerName": "R5EBUILDAGENT02",
                        "Agent.HomeDirectory": "C:\\vststestAgt1",
                        "Agent.OS": "Windows_NT",
                        "Agent.OSVersion": "10.0.14393",
                        */

                    csv.WriteField("AgentId");
                    csv.WriteField("Agent Name");
                    csv.WriteField("Pool");
                    csv.WriteField("Enabled");
                    csv.WriteField("Status");
                    csv.WriteField("Version");
                    csv.WriteField("OS");
                    csv.WriteField("OS Version");
                    csv.WriteField("Computer");
                    csv.WriteField("HomeDirectory");
                    csv.NextRecord();

                    foreach (var agentPool in agentPools)
                    {
                        foreach (var agent in agentPool.Agents)
                        {
                            csv.WriteField(agent.Id);
                            csv.WriteField(agent.Name);
                            csv.WriteField(agentPool.Name);
                            csv.WriteField(agent.Enabled);
                            csv.WriteField(agent.Status);
                            csv.WriteField(agent.Version);
                            csv.WriteField(agent.OS);
                            csv.WriteField(agent.OSVersion);
                            csv.WriteField(agent.ComputerName);
                            csv.WriteField(agent.HomeDirectory);

                            csv.NextRecord();
                        }
                    }

                    csv.Flush();
                }


            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(ex);
            }
        }

        public static List<AgentPool> GetAgentPools()
        {
            var agentPools = new List<AgentPool>();

            using (var response = httpClient.GetAsync($"{baseUrl}/_apis/distributedtask/pools").Result)
            {
                response.EnsureSuccessStatusCode();
                string responseBody = response.Content.ReadAsStringAsync().Result;

                var jObject = JObject.Parse(responseBody);

                foreach (var agentPoolJson in jObject["value"])
                {
                    var agentPool = new AgentPool
                    {
                        Id = agentPoolJson["id"].ToString(),
                        Name = agentPoolJson["name"].ToString()
                    };

                    var agents = GetAgentsInPool(agentPool);
                    agentPool.Agents = agents;

                    agentPools.Add(agentPool);
                }
            }

            return agentPools;
        }

        public static List<Agent> GetAgentsInPool(AgentPool pool)
        {
            var agents = new List<Agent>();

            using (var response = httpClient.GetAsync($"{baseUrl}/_apis/distributedtask/pools/{pool.Id}/agents?includeCapabilities=true").Result)
            {
                response.EnsureSuccessStatusCode();
                string responseBody = response.Content.ReadAsStringAsync().Result;

                var jObject = JObject.Parse(responseBody);

                foreach (var agentsJson in jObject["value"])
                {
                    var agent = new Agent
                    {
                        Id = agentsJson["id"].ToString(),
                        Name = agentsJson["systemCapabilities"]["Agent.Name"].ToString(),
                        Version = agentsJson["version"].ToString(),
                        Enabled = agentsJson["enabled"].ToString(),
                        Status = agentsJson["status"].ToString(),

                        /*
                         * "id": 4,
                            "name": "R5EBUILDAGENT02",
                            "version": "2.134.2",
                            "osDescription": "Microsoft Windows 10.0.14393 ",
                            "enabled": true,
                            "status": "offline",

                            "Agent.Name": "vststestAgt1",
                            "Agent.Version": "2.134.2",
                            "Agent.ComputerName": "R5EBUILDAGENT02",
                            "Agent.HomeDirectory": "C:\\vststestAgt1",
                            "Agent.OS": "Windows_NT",
                            "Agent.OSVersion": "10.0.14393",
                            */
                    };

                    var capabilities = agentsJson["systemCapabilities"];
                    if (capabilities["Agent.ComputerName"] != null)
                    {
                        agent.ComputerName = agentsJson["systemCapabilities"]["Agent.ComputerName"].ToString();
                    }

                    if (capabilities["Agent.HomeDirectory"] != null)
                    {
                        agent.HomeDirectory = agentsJson["systemCapabilities"]["Agent.HomeDirectory"].ToString();
                    }

                    if (capabilities["Agent.OS"] != null)
                    {
                        agent.OS = agentsJson["systemCapabilities"]["Agent.OS"].ToString();
                    }

                    if (capabilities["Agent.OSVersion"] != null)
                    {
                        agent.OSVersion = agentsJson["systemCapabilities"]["Agent.OSVersion"].ToString();
                    }

                    agents.Add(agent);
                }
            }

            return agents;
        }

        public static List<Project> GetProjects()
        {
            var projects = new List<Project>();

            using (var response = httpClient.GetAsync($"{baseUrl}/_apis/projects").Result)
            {
                response.EnsureSuccessStatusCode();
                string responseBody = response.Content.ReadAsStringAsync().Result;

                var jObject = JObject.Parse(responseBody);

                foreach (var projectObject in jObject["value"])
                {
                    var project = new Project
                    {
                        Id = projectObject["id"].ToString(),
                        Name = projectObject["name"].ToString()
                    };

                    projects.Add(project);
                }
            }

            return projects;
        }

        public static List<Build> GetBuilds(List<Project> projects)
        {
            var builds = new List<Build>();

            foreach (var project in projects)
            {
                using (var response = httpClient.GetAsync($"{baseUrl}/{project.Name}/_apis/build/definitions?includeAllProperties=true").Result)
                {
                    response.EnsureSuccessStatusCode();
                    string responseBody = response.Content.ReadAsStringAsync().Result;

                    var jObject = JObject.Parse(responseBody);

                    foreach (var buildObject in jObject["value"])
                    {
                        var build = new Build
                        {
                            Id = buildObject["id"].ToString(),
                            Name = buildObject["name"].ToString(),
                            Path = buildObject["path"].ToString(),
                            Project = project,
                            RepositoryName = buildObject["repository"]["name"].ToString(),
                            DefaultBranch = buildObject["repository"]["defaultBranch"].ToString(),
                            QueueId = buildObject["queue"]["id"].ToString()
                        };

                        builds.Add(build);
                    }
                }
            }

            return builds;
        }

        //https://{accountName}.vsrm.visualstudio.com/{project}/_apis/release/definitions

        public static List<AgentQueue> GetAgentQueues(List<Project> projects)
        {
            var agentQueues = new List<AgentQueue>();

            foreach (var project in projects)
            {
                using (var response = httpClient.GetAsync($"{baseUrl}/{project.Id}/_apis/distributedtask/queues").Result)
                {
                    response.EnsureSuccessStatusCode();
                    string responseBody = response.Content.ReadAsStringAsync().Result;

                    var jObject = JObject.Parse(responseBody);

                    foreach (var agentQueuesJson in jObject["value"])
                    {
                        var build = new AgentQueue
                        {
                            Id = agentQueuesJson["id"].ToString(),
                            Name = agentQueuesJson["name"].ToString(),
                            PoolId = agentQueuesJson["pool"]["id"].ToString(),
                            ProjectId = agentQueuesJson["projectId"].ToString()
                        };

                        agentQueues.Add(build);
                    }
                }
            }

            return agentQueues;
        }
    }

    public class Build
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public string RepositoryName { get; set; }
        public string DefaultBranch { get; set; }
        public Project Project { get; set; }
        public string QueueId { get; set; }

        public AgentQueue Queue { get; set; }
    }

    public class Release
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }

        public Project Project { get; set; }
        public string QueueId { get; set; }

        public AgentQueue Queue { get; set; }
    }

    public class Project
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }

    public class AgentPool
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public List<Agent> Agents { get; set; }
    }

    public class AgentQueue
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string PoolId { get; set; }
        public string ProjectId { get; set; }

        public AgentPool AgentPool { get; set; }
        public Project Project { get; set;}
    }

    public class Agent
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }
        public string Enabled { get; set; }
        public string Status { get; set; }
        public string ComputerName { get; set; }
        public string HomeDirectory { get; set; }
        public string OS { get; set; }
        public string OSVersion { get; set; }

        /*
         * "id": 4,
            "name": "R5EBUILDAGENT02",
            "version": "2.134.2",
            "osDescription": "Microsoft Windows 10.0.14393 ",
            "enabled": true,
            "status": "offline",

            "Agent.Name": "vststestAgt1",
            "Agent.Version": "2.134.2",
            "Agent.ComputerName": "R5EBUILDAGENT02",
            "Agent.HomeDirectory": "C:\\vststestAgt1",
            "Agent.OS": "Windows_NT",
            "Agent.OSVersion": "10.0.14393",
            */
    }
}
