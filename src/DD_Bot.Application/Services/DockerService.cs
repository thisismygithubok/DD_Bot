using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DD_Bot.Application.Interfaces;
using DD_Bot.Domain;
using Microsoft.Extensions.Configuration;
using Docker.DotNet;
using Docker.DotNet.Models;
using Timer = System.Timers.Timer;

namespace DD_Bot.Application.Services
{
    public class DockerService : IDockerService
    {
        private readonly IConfigurationRoot _configuration;
        private IList<ContainerListResponse> _dockerResponse;
        public List<ContainerListResponse> DockerStatus { get; private set; }
        private readonly DockerClient _client = new DockerClientConfiguration(
                new Uri("unix:///var/run/docker.sock"))
            .CreateClient();

        public DockerSettings Settings => _configuration.Get<Settings>().DockerSettings;
        public DockerService(IConfigurationRoot configuration) // Initialising
        {
            _configuration = configuration;
            DockerUpdate().Wait(); // Ensure DockerUpdate completes before continuing
            var updateTimer = new Timer();
            updateTimer.Interval = TimeSpan.FromMinutes(5).TotalMilliseconds;
            updateTimer.Elapsed += async (s, e) => await DockerUpdate(); // Await DockerUpdate
            updateTimer.AutoReset = true;
            updateTimer.Start();
        }

        public string[] RunningDockers => DockerStatus.Where(docker => docker.Status.Contains("Up")).Select(pairs => pairs.Names[0]).ToArray();
        public string[] StoppedDockers => DockerStatus.Where(docker => !docker.Status.Contains("Up")).Select(pairs => pairs.Names[0]).ToArray();
     
        public async Task DockerUpdate() // Update
        {
            _dockerResponse = await _client.Containers.ListContainersAsync(new ContainersListParameters(){ All = true });
            DockerStatus = new List<ContainerListResponse>();
            foreach (var variable in _dockerResponse)
            {
                DockerStatus.Add(variable);
            }
            
            if (DockerStatus == null) return;
            {
                foreach (var variable in DockerStatus)
                {
                    variable.Names[0] = variable.Names[0].Substring(1);
                }
            }
            DockerContainerSort();
        }

        private void DockerContainerSort()
        {
            DockerStatus.Sort((x, y) => String.Compare(x.Names[0], y.Names[0], StringComparison.Ordinal));
        }

        public int DockerStatusLongestName()
        {
            int counter = 0;
            foreach (var item in DockerStatus)
            {
                if (item.Names[0].Length > counter)
                {
                    counter = item.Names[0].Length;
                }
            }
            return counter;
        }

        public async Task DockerCommandStart(string id)
        {
            await _client.Containers.StartContainerAsync(id, new ContainerStartParameters());
        }

        public async Task DockerCommandStop(string id)
        {
            await _client.Containers.StopContainerAsync(id, new ContainerStopParameters());
        }

        public async Task DockerCommandRestart(string id)
        {
            await _client.Containers.RestartContainerAsync(id, new ContainerRestartParameters());
        }

        // New method to get containers by section
        public List<string> GetContainersBySection(string section)
        {
            return DockerStatus
                .Where(container => container.Labels.ContainsKey("section") && container.Labels["section"] == section)
                .Select(container => container.Names[0])
                .ToList();
        }
        
        public void Start()
        {
            Console.WriteLine("DockerService started");
        }
    }
}