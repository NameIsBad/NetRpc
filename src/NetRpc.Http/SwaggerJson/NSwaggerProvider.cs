﻿using System.Collections.Generic;
using System.Linq;
using Microsoft.OpenApi.Models;
using NetRpc.Contract;

namespace NetRpc.Http
{
    internal class NSwaggerProvider : INSwaggerProvider
    {
        private readonly PathProcessor _pathProcessor;
        private readonly SwaggerKeyRoles _keyRoles;
        private readonly OpenApiDocument _doc;

        public NSwaggerProvider(PathProcessor pathProcessor, SwaggerKeyRoles keyRoles)
        {
            _pathProcessor = pathProcessor;
            _keyRoles = keyRoles;
            _doc = new OpenApiDocument();
        }

        public OpenApiDocument GetSwagger(string? apiRootPath, List<ContractInfo> contracts, string? key)
        {
            Process(apiRootPath, contracts, key);
            return _doc;
        }

        private void Process(string? apiRootPath, List<ContractInfo> contracts, string? key)
        {
            //tags
            ProcessTags(contracts);

            //path
            ProcessPath(apiRootPath, contracts, key);

            //Components
            ProcessComponents(contracts);
        }

        private void ProcessTags(List<ContractInfo> contracts)
        {
            var tags = new List<string>();
            contracts.ForEach(i => tags.AddRange(i.Tags));
            var distTags = tags.Distinct();
            foreach (var distTag in distTags)
                _doc.Tags.Add(new OpenApiTag { Name = distTag });
        }

        private void ProcessComponents(List<ContractInfo> contracts)
        {
            //Schemas
            _doc.Components = new OpenApiComponents
            {
                Schemas = _pathProcessor.SchemaRepository.Schemas
            };

            //SecurityScheme
            var dic = new Dictionary<string, SecurityApiKeyDefineAttribute>();
            contracts.ForEach(i => i.SecurityApiKeyDefineAttributes.ToList().ForEach(j => dic[j.Key] = j));
            foreach (var item in dic.Values)
            {
                var scheme = new OpenApiSecurityScheme
                {
                    Description = item.Description,
                    Name = item.Name,
                    Type = SecuritySchemeType.ApiKey,
                    In = ParameterLocation.Header,
                    UnresolvedReference = false
                };
                _doc.Components.SecuritySchemes[item.Key] = scheme;
            }
        }

        private void ProcessPath(string? apiRootPath, List<ContractInfo> contracts, string? key)
        {
            _doc.Paths = new OpenApiPaths();
            foreach (var contract in contracts)
            {
                var roles = _keyRoles.GetRoles(key);
                var roleMethods = contract.GetMethods(roles);
                foreach (var contractMethod in roleMethods)
                {
                    foreach (var route in contractMethod.Route.SwaggerRouts)
                    {
                        var pathItem = new OpenApiPathItem();
                        foreach (var method in route.HttpMethods)
                        {
                            //AddOperation 
                            var operation = _pathProcessor.Process(contractMethod, route, method);
                            pathItem.AddOperation(method.ToOperationType(), operation);
                        }

                        //add a path
                        _doc.Paths.Add($"{apiRootPath}/{route.Path}", pathItem);
                    }
                }
            }
        }
    }
}