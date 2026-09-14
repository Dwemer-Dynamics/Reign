$path = 'C:\Users\speed\Desktop\ReignBetaServer\Program.cs'
$text = [System.IO.File]::ReadAllText($path)

$oldRoutes = @'
                    else if (request.Method == "POST" && request.Path == "/actions/propose")
                    {
                        response = ProposeAction(request.JsonBody);
                    }
                    else if (request.Method == "POST" && request.Path == "/llm/chat")
                    {
                        response = new Dictionary<string, object>
                        {
                            ["ok"] = false,
                            ["error"] = "NanoGPT routing is reserved for the next ReignBeta server build."
                        };
                    }
'@

$newRoutes = @'
                    else if (request.Method == "GET" && request.Path == "/actions/catalog")
                    {
                        response = ActionCatalog();
                    }
                    else if (request.Method == "GET" && request.Path == "/actions/next")
                    {
                        response = NextActions(request.JsonBody, request.Query);
                    }
                    else if (request.Method == "POST" && request.Path == "/actions/next")
                    {
                        response = NextActions(request.JsonBody, request.Query);
                    }
                    else if (request.Method == "POST" && request.Path == "/actions/propose")
                    {
                        response = ProposeAction(request.JsonBody);
                    }
                    else if (request.Method == "POST" && request.Path == "/actions/decide")
                    {
                        response = DecideActions(request.JsonBody);
                    }
                    else if (request.Method == "POST" && request.Path == "/actions/report")
                    {
                        response = ReportAction(request.JsonBody);
                    }
                    else if (request.Method == "POST" && request.Path == "/llm/chat")
                    {
                        response = ChatWithLlm(request.JsonBody);
                    }
'@

if (-not $text.Contains($oldRoutes)) {
    throw 'Expected route block was not found.'
}

$text = $text.Replace($oldRoutes, $newRoutes)
$start = $text.IndexOf('        private static Dictionary<string, object> ProposeAction(Dictionary<string, object> payload)')
$end = $text.IndexOf('        private static Dictionary<string, object> DefaultSettings()', $start)
if ($start -lt 0 -or $end -lt 0) {
    throw 'Could not locate ProposeAction/DefaultSettings block.'
}

$newBlock = @'
        private static Dictionary<string, object> ActionCatalog()
        {
            List<Dictionary<string, object>> commands = new List<Dictionary<string, object>>
            {
                CatalogCommand("declare_war", "diplomacy", "DiplomacyDeclareWar", "actorKingdomId,targetKingdomId,reason", "Actor kingdom declares war on target kingdom."),
                CatalogCommand("make_peace", "diplomacy", "DiplomacyMakePeace", "actorKingdomId,targetKingdomId,reason", "Actor kingdom makes peace with target kingdom."),
                CatalogCommand("offer_tribute_peace", "diplomacy", "DiplomacyOfferTributePeace", "actorKingdomId,targetKingdomId,reason,terms.dailyTribute,terms.durationDays", "Actor kingdom makes peace with tribute terms."),
                CatalogCommand("record_promise", "diplomacy", "DiplomacyRecordPromise", "actorKingdomId,targetKingdomId,reason", "Records a diplomatic promise or obligation for future memory."),
                CatalogCommand("recruit_and_recover", "strategy", "StrategyRecruitAndRecover", "actorHeroId,targetSettlementId,reason,minimumTroops", "Lord party recruits, gathers food, and recovers before a later order."),
                CatalogCommand("form_army", "strategy", "StrategyFormArmy", "actorHeroId,targetSettlementId,reason,desiredStrength", "Lord attempts to form an army near a target."),
                CatalogCommand("attack_settlement", "strategy", "StrategyAttackSettlement", "actorHeroId,targetSettlementId,reason", "Lord moves to raid or besiege a hostile settlement."),
                CatalogCommand("capture_settlement_plan", "strategy", "StrategyCaptureSettlement", "actorHeroId,targetSettlementId,reason,minimumTroops,desiredStrength", "Multi-stage plan: validate war, recruit, form army, move, and besiege."),
            };

            return new Dictionary<string, object>
            {
                ["ok"] = true,
                ["version"] = "0.1",
                ["format"] = "OpenAI-compatible LLMs should return JSON: { \"actions\": [ { \"command\": \"capture_settlement_plan\", ... } ] }",
                ["commands"] = commands
            };
        }

        private static Dictionary<string, object> CatalogCommand(string command, string family, string actionType, string required, string description)
        {
            return new Dictionary<string, object>
            {
                ["command"] = command,
                ["family"] = family,
                ["mapsTo"] = actionType,
                ["required"] = required.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList(),
                ["optional"] = new List<string> { "source", "requiresAcceptance", "executeAfterDays", "maxAttempts", "terms" },
                ["description"] = description
            };
        }

        private static Dictionary<string, object> ProposeAction(Dictionary<string, object> payload)
        {
            payload = payload ?? new Dictionary<string, object>();
            string campaignId = ReadString(payload, "campaignId", "default");
            List<Dictionary<string, object>> inputs = ReadDictionaryList(payload, "actions");
            Dictionary<string, object> singleAction = ReadDictionary(payload, "action");
            if (singleAction != null)
            {
                inputs.Add(singleAction);
            }

            if (inputs.Count == 0)
            {
                inputs.Add(payload);
            }

            List<Dictionary<string, object>> queued = new List<Dictionary<string, object>>();
            List<string> errors = new List<string>();
            foreach (Dictionary<string, object> input in inputs)
            {
                Dictionary<string, object> normalized = NormalizeActionCommand(input, campaignId, out List<string> actionErrors);
                if (actionErrors.Count > 0)
                {
                    errors.AddRange(actionErrors);
                    continue;
                }

                queued.Add(QueueAction(campaignId, normalized));
            }

            if (errors.Count > 0)
            {
                LogOperational("action.reject", new Dictionary<string, object> { ["campaignId"] = campaignId, ["errors"] = errors });
                return new Dictionary<string, object>
                {
                    ["ok"] = false,
                    ["errors"] = errors,
                    ["catalog"] = ActionCatalog()
                };
            }

            LogOperational("action.propose", new Dictionary<string, object>
            {
                ["campaignId"] = campaignId,
                ["count"] = queued.Count
            });

            return new Dictionary<string, object>
            {
                ["ok"] = true,
                ["queuedActions"] = queued,
                ["count"] = queued.Count
            };
        }

        private static Dictionary<string, object> DecideActions(Dictionary<string, object> payload)
        {
            payload = payload ?? new Dictionary<string, object>();
            string campaignId = ReadString(payload, "campaignId", "default");
            Dictionary<string, object> context = BuildContext(payload);
            object memories = context.ContainsKey("memories") ? context["memories"] : new List<object>();
            object worldState = payload.ContainsKey("worldState") ? payload["worldState"] : payload;
            string requestType = ReadString(payload, "requestType", "strategy");

            Dictionary<string, object> catalog = ActionCatalog();
            List<Dictionary<string, object>> messages = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object>
                {
                    ["role"] = "system",
                    ["content"] = "You are ReignBeta, a Bannerlord AI world director. Return strict JSON only. Use only the supplied action catalog. Prefer believable character and kingdom motives. Never invent IDs; use only IDs present in the world state."
                },
                new Dictionary<string, object>
                {
                    ["role"] = "user",
                    ["content"] = "Action catalog:\n" + Json.Serialize(catalog["commands"]) + "\n\nRelevant memories:\n" + Json.Serialize(memories) + "\n\nWorld state/request:\n" + Json.Serialize(worldState) + "\n\nReturn JSON like {\"actions\":[{\"command\":\"capture_settlement_plan\",\"actorHeroId\":\"...\",\"targetSettlementId\":\"...\",\"reason\":\"...\"}]}"
                }
            };

            Dictionary<string, object> llmPayload = new Dictionary<string, object>
            {
                ["requestType"] = requestType,
                ["messages"] = messages,
                ["response_format"] = new Dictionary<string, object> { ["type"] = "json_object" }
            };

            Dictionary<string, object> llm = ChatWithLlm(llmPayload);
            if (!ReadBool(llm, "ok", false))
            {
                return new Dictionary<string, object> { ["ok"] = false, ["stage"] = "llm", ["llm"] = llm };
            }

            string content = ReadString(llm, "content", "");
            Dictionary<string, object> parsed = TryParseJsonObject(content);
            if (parsed == null)
            {
                return new Dictionary<string, object> { ["ok"] = false, ["stage"] = "parse", ["content"] = content };
            }

            Dictionary<string, object> proposePayload = new Dictionary<string, object>
            {
                ["campaignId"] = campaignId,
                ["actions"] = ReadDictionaryList(parsed, "actions")
            };

            Dictionary<string, object> proposed = ProposeAction(proposePayload);
            proposed["llm"] = new Dictionary<string, object>
            {
                ["requestType"] = requestType,
                ["model"] = ReadString(llm, "model", ""),
                ["content"] = content
            };
            return proposed;
        }

        private static Dictionary<string, object> ChatWithLlm(Dictionary<string, object> payload)
        {
            payload = payload ?? new Dictionary<string, object>();
            Dictionary<string, object> settings = LoadSettings();
            string apiUrl = ReadString(payload, "apiUrl", ReadString(settings, "apiUrl", ""));
            string apiKey = ReadString(payload, "apiKey", ReadString(settings, "apiKey", ""));
            string requestType = ReadString(payload, "requestType", "dialogue");
            string model = ReadString(payload, "model", ModelForRequest(settings, requestType));

            if (string.IsNullOrWhiteSpace(apiUrl))
            {
                return new Dictionary<string, object> { ["ok"] = false, ["error"] = "API URL is empty." };
            }

            List<Dictionary<string, object>> messages = ReadDictionaryList(payload, "messages");
            if (messages.Count == 0)
            {
                string system = ReadString(payload, "system", "You are ReignBeta, a Bannerlord AI world assistant.");
                string prompt = ReadString(payload, "prompt", ReadString(payload, "text", ""));
                messages.Add(new Dictionary<string, object> { ["role"] = "system", ["content"] = system });
                messages.Add(new Dictionary<string, object> { ["role"] = "user", ["content"] = prompt });
            }

            Dictionary<string, object> requestBody = new Dictionary<string, object>
            {
                ["model"] = model,
                ["messages"] = messages,
                ["temperature"] = ReadDouble(payload, "temperature", ReadDouble(settings, "temperature", 0.7d)),
                ["max_tokens"] = ReadInt(payload, "maxTokens", ReadInt(settings, "maxTokens", 900)),
                ["stream"] = false
            };

            CopyOptional(payload, requestBody, "response_format");
            CopyOptional(payload, requestBody, "tools");
            CopyOptional(payload, requestBody, "tool_choice");
            CopyOptional(payload, requestBody, "stop");
            CopyOptional(payload, requestBody, "seed");
            Dictionary<string, object> extra = ReadDictionary(payload, "extra");
            if (extra != null)
            {
                foreach (KeyValuePair<string, object> pair in extra)
                {
                    requestBody[pair.Key] = pair.Value;
                }
            }

            try
            {
                string responseText = PostJsonToLlm(apiUrl, apiKey, Json.Serialize(requestBody), settings);
                Dictionary<string, object> raw = Json.Deserialize<Dictionary<string, object>>(responseText);
                string content = ExtractAssistantContent(raw);
                Dictionary<string, object> result = new Dictionary<string, object>
                {
                    ["ok"] = true,
                    ["provider"] = "openai_compatible",
                    ["requestType"] = requestType,
                    ["model"] = model,
                    ["content"] = content,
                    ["raw"] = raw
                };

                LogLlm(requestType, model, requestBody, raw, true, null);
                return result;
            }
            catch (Exception ex)
            {
                LogLlm(requestType, model, requestBody, new Dictionary<string, object> { ["error"] = ex.Message }, false, ex.Message);
                return new Dictionary<string, object>
                {
                    ["ok"] = false,
                    ["provider"] = "openai_compatible",
                    ["requestType"] = requestType,
                    ["model"] = model,
                    ["error"] = ex.Message
                };
            }
        }

        private static string PostJsonToLlm(string apiUrl, string apiKey, string requestJson, Dictionary<string, object> settings)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(apiUrl);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Accept = "application/json";
            request.Timeout = 120000;
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                request.Headers["Authorization"] = "Bearer " + apiKey;
            }

            string siteUrl = ReadString(settings, "siteUrl", "");
            string appTitle = ReadString(settings, "appTitle", "ReignBeta");
            if (!string.IsNullOrWhiteSpace(siteUrl))
            {
                request.Headers["HTTP-Referer"] = siteUrl;
                request.Headers["Referer"] = siteUrl;
            }

            if (!string.IsNullOrWhiteSpace(appTitle))
            {
                request.Headers["X-Title"] = appTitle;
            }

            byte[] bytes = Encoding.UTF8.GetBytes(requestJson);
            request.ContentLength = bytes.Length;
            using (Stream requestStream = request.GetRequestStream())
            {
                requestStream.Write(bytes, 0, bytes.Length);
            }

            try
            {
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream responseStream = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(responseStream ?? Stream.Null, Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
            catch (WebException ex)
            {
                string body = "";
                if (ex.Response != null)
                {
                    using (Stream responseStream = ex.Response.GetResponseStream())
                    using (StreamReader reader = new StreamReader(responseStream ?? Stream.Null, Encoding.UTF8))
                    {
                        body = reader.ReadToEnd();
                    }
                }

                throw new InvalidOperationException(string.IsNullOrWhiteSpace(body) ? ex.Message : body, ex);
            }
        }

        private static string ExtractAssistantContent(Dictionary<string, object> raw)
        {
            if (raw == null || !raw.TryGetValue("choices", out object choicesValue) || !(choicesValue is ArrayList choices) || choices.Count == 0)
            {
                return "";
            }

            Dictionary<string, object> first = choices[0] as Dictionary<string, object>;
            if (first == null)
            {
                return "";
            }

            Dictionary<string, object> message = first.ContainsKey("message") ? first["message"] as Dictionary<string, object> : null;
            string content = ReadString(message, "content", "");
            return string.IsNullOrWhiteSpace(content) ? ReadString(first, "text", "") : content;
        }

        private static void LogLlm(string requestType, string model, Dictionary<string, object> requestBody, Dictionary<string, object> responseBody, bool ok, string error)
        {
            Dictionary<string, object> settings = LoadSettings();
            if (!ReadBool(settings, "enableLlmLogging", true))
            {
                return;
            }

            Dictionary<string, object> row = new Dictionary<string, object>
            {
                ["ts"] = DateTimeOffset.UtcNow.ToString("o"),
                ["ok"] = ok,
                ["requestType"] = requestType,
                ["model"] = model,
                ["request"] = requestBody,
                ["response"] = responseBody
            };
            if (!string.IsNullOrWhiteSpace(error))
            {
                row["error"] = error;
            }

            lock (FileLock)
            {
                Directory.CreateDirectory(LogsDir);
                File.AppendAllText(Path.Combine(LogsDir, "llm-log.jsonl"), Json.Serialize(row) + Environment.NewLine, Encoding.UTF8);
            }
        }

        private static Dictionary<string, object> NormalizeActionCommand(Dictionary<string, object> raw, string campaignId, out List<string> errors)
        {
            raw = raw ?? new Dictionary<string, object>();
            errors = new List<string>();
            string command = CanonicalCommand(ReadFirstString(raw, "command", "commandType", "intent", "type"));
            string mappedType = MapCommandToActionType(command);
            if (string.IsNullOrWhiteSpace(mappedType))
            {
                errors.Add("Unsupported action command: " + (string.IsNullOrWhiteSpace(command) ? "<missing>" : command));
                return null;
            }

            string actionId = ReadFirstString(raw, "actionId", "id", "serverActionId");
            if (string.IsNullOrWhiteSpace(actionId))
            {
                actionId = Guid.NewGuid().ToString("N");
            }

            Dictionary<string, object> terms = ReadDictionary(raw, "terms");
            string termsJson = terms == null ? ReadString(raw, "termsJson", "") : Json.Serialize(terms);
            Dictionary<string, object> record = new Dictionary<string, object>
            {
                ["actionId"] = actionId,
                ["serverActionId"] = actionId,
                ["campaignId"] = campaignId,
                ["command"] = command,
                ["type"] = mappedType,
                ["source"] = ReadString(raw, "source", "server_llm_or_manual"),
                ["actorHeroStringId"] = ReadFirstString(raw, "actorHeroStringId", "actorHeroId", "leaderHeroId", "heroId"),
                ["actorKingdomStringId"] = ReadFirstString(raw, "actorKingdomStringId", "actorKingdomId", "kingdomId"),
                ["targetHeroStringId"] = ReadFirstString(raw, "targetHeroStringId", "targetHeroId"),
                ["targetKingdomStringId"] = ReadFirstString(raw, "targetKingdomStringId", "targetKingdomId"),
                ["targetSettlementStringId"] = ReadFirstString(raw, "targetSettlementStringId", "targetSettlementId", "settlementId"),
                ["reason"] = ReadString(raw, "reason", "AI world action from ReignBeta server."),
                ["termsJson"] = termsJson,
                ["minimumTroops"] = ReadInt(raw, "minimumTroops", 40),
                ["desiredStrength"] = ReadInt(raw, "desiredStrength", 350),
                ["maxAttempts"] = ReadInt(raw, "maxAttempts", mappedType == "StrategyCaptureSettlement" ? 24 : 3),
                ["executeAfterDays"] = ReadDouble(raw, "executeAfterDays", 0d),
                ["requiresAcceptance"] = ReadBool(raw, "requiresAcceptance", false)
            };

            if (mappedType.StartsWith("Diplomacy", StringComparison.OrdinalIgnoreCase))
            {
                Require(record, "actorKingdomStringId", errors);
                Require(record, "targetKingdomStringId", errors);
            }
            else if (mappedType.StartsWith("Strategy", StringComparison.OrdinalIgnoreCase))
            {
                Require(record, "actorHeroStringId", errors);
                Require(record, "targetSettlementStringId", errors);
            }

            if (string.IsNullOrWhiteSpace(ReadString(record, "reason", "")))
            {
                errors.Add("Action reason is required.");
            }

            return errors.Count == 0 ? record : null;
        }

        private static Dictionary<string, object> QueueAction(string campaignId, Dictionary<string, object> record)
        {
            string id = ReadString(record, "serverActionId", Guid.NewGuid().ToString("N"));
            Dictionary<string, object> row = new Dictionary<string, object>
            {
                ["id"] = id,
                ["ts"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ["campaignId"] = campaignId,
                ["status"] = "queued",
                ["command"] = ReadString(record, "command", ""),
                ["record"] = record
            };

            lock (FileLock)
            {
                List<Dictionary<string, object>> queue = ReadActionQueueUnlocked();
                queue.RemoveAll(x => ReadString(x, "id", "") == id);
                queue.Add(row);
                if (queue.Count > 500)
                {
                    queue = queue.Skip(queue.Count - 500).ToList();
                }

                WriteActionQueueUnlocked(queue);
                File.AppendAllText(Path.Combine(DataDir, "actions.jsonl"), Json.Serialize(row) + Environment.NewLine, Encoding.UTF8);
            }

            return row;
        }

        private static Dictionary<string, object> NextActions(Dictionary<string, object> payload, Dictionary<string, string> query)
        {
            payload = payload ?? new Dictionary<string, object>();
            query = query ?? new Dictionary<string, string>();
            string campaignId = query.TryGetValue("campaignId", out string campaignQuery) ? campaignQuery : ReadString(payload, "campaignId", "default");
            int limit = query.TryGetValue("limit", out string limitText) && int.TryParse(limitText, out int parsedLimit) ? parsedLimit : ReadInt(payload, "limit", 10);
            limit = Math.Max(1, Math.Min(50, limit));
            List<Dictionary<string, object>> selected = new List<Dictionary<string, object>>();

            lock (FileLock)
            {
                List<Dictionary<string, object>> queue = ReadActionQueueUnlocked();
                foreach (Dictionary<string, object> row in queue)
                {
                    string status = ReadString(row, "status", "queued");
                    if (selected.Count >= limit)
                    {
                        break;
                    }

                    if (!ReadString(row, "campaignId", "default").Equals(campaignId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!status.Equals("queued", StringComparison.OrdinalIgnoreCase) && !status.Equals("proposed", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    row["status"] = "delivered";
                    row["deliveredTs"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    selected.Add(row);
                }

                WriteActionQueueUnlocked(queue);
            }

            List<object> records = selected.Select(x => x.ContainsKey("record") ? x["record"] : x).ToList();
            LogOperational("action.next", new Dictionary<string, object> { ["campaignId"] = campaignId, ["count"] = records.Count });
            return new Dictionary<string, object>
            {
                ["ok"] = true,
                ["campaignId"] = campaignId,
                ["count"] = records.Count,
                ["actions"] = records
            };
        }

        private static Dictionary<string, object> ReportAction(Dictionary<string, object> payload)
        {
            payload = payload ?? new Dictionary<string, object>();
            string id = ReadFirstString(payload, "serverActionId", "actionId", "id");
            string status = ReadString(payload, "status", "reported");
            if (string.IsNullOrWhiteSpace(id))
            {
                return new Dictionary<string, object> { ["ok"] = false, ["error"] = "serverActionId/actionId is required." };
            }

            bool found = false;
            lock (FileLock)
            {
                List<Dictionary<string, object>> queue = ReadActionQueueUnlocked();
                foreach (Dictionary<string, object> row in queue)
                {
                    if (ReadString(row, "id", "") == id)
                    {
                        row["status"] = status;
                        row["reportTs"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                        row["report"] = payload;
                        found = true;
                        break;
                    }
                }

                WriteActionQueueUnlocked(queue);
                File.AppendAllText(Path.Combine(DataDir, "actions.jsonl"), Json.Serialize(new Dictionary<string, object>
                {
                    ["id"] = id,
                    ["ts"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    ["status"] = status,
                    ["report"] = payload
                }) + Environment.NewLine, Encoding.UTF8);
            }

            LogOperational("action.report", new Dictionary<string, object> { ["id"] = id, ["status"] = status, ["found"] = found });
            return new Dictionary<string, object> { ["ok"] = true, ["id"] = id, ["status"] = status, ["found"] = found };
        }

        private static List<Dictionary<string, object>> ReadActionQueueUnlocked()
        {
            string path = Path.Combine(DataDir, "action-queue.json");
            if (!File.Exists(path))
            {
                return new List<Dictionary<string, object>>();
            }

            try
            {
                object value = Json.DeserializeObject(File.ReadAllText(path, Encoding.UTF8));
                if (value is ArrayList array)
                {
                    return array.Cast<object>().OfType<Dictionary<string, object>>().ToList();
                }
            }
            catch
            {
            }

            return new List<Dictionary<string, object>>();
        }

        private static void WriteActionQueueUnlocked(List<Dictionary<string, object>> queue)
        {
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(Path.Combine(DataDir, "action-queue.json"), Json.Serialize(queue ?? new List<Dictionary<string, object>>()), Encoding.UTF8);
        }

        private static string ModelForRequest(Dictionary<string, object> settings, string requestType)
        {
            string key;
            switch ((requestType ?? "").ToLowerInvariant())
            {
                case "diplomacy": key = "diplomacyModel"; break;
                case "events": key = "eventsModel"; break;
                case "memory": key = "memoryModel"; break;
                case "strategy":
                case "action":
                case "actions": key = "strategyModel"; break;
                default: key = "dialogueModel"; break;
            }

            string model = ReadString(settings, key, "");
            return string.IsNullOrWhiteSpace(model) ? ReadString(settings, "dialogueModel", "") : model;
        }

        private static string CanonicalCommand(string command)
        {
            string value = (command ?? "").Trim();
            if (value.Length == 0)
            {
                return "";
            }

            value = value.Replace("-", "_").Replace(" ", "_");
            switch (value.ToLowerInvariant())
            {
                case "diplomacydeclarewar":
                case "diplomacy_declare_war":
                case "declarewar": return "declare_war";
                case "diplomacymakepeace":
                case "diplomacy_make_peace":
                case "makepeace": return "make_peace";
                case "diplomacyoffertributepeace":
                case "offertributepeace": return "offer_tribute_peace";
                case "diplomacyrecordpromise":
                case "recordpromise": return "record_promise";
                case "strategyrecruitandrecover": return "recruit_and_recover";
                case "strategyformarmy": return "form_army";
                case "strategyattacksettlement": return "attack_settlement";
                case "strategycapturesettlement":
                case "capture_settlement": return "capture_settlement_plan";
                default: return value.ToLowerInvariant();
            }
        }

        private static string MapCommandToActionType(string command)
        {
            switch (CanonicalCommand(command))
            {
                case "declare_war": return "DiplomacyDeclareWar";
                case "make_peace": return "DiplomacyMakePeace";
                case "offer_tribute_peace": return "DiplomacyOfferTributePeace";
                case "record_promise": return "DiplomacyRecordPromise";
                case "recruit_and_recover": return "StrategyRecruitAndRecover";
                case "form_army": return "StrategyFormArmy";
                case "attack_settlement": return "StrategyAttackSettlement";
                case "capture_settlement_plan": return "StrategyCaptureSettlement";
                default: return "";
            }
        }

        private static void Require(Dictionary<string, object> record, string key, List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(ReadString(record, key, "")))
            {
                errors.Add(key + " is required.");
            }
        }

        private static void CopyOptional(Dictionary<string, object> source, Dictionary<string, object> target, string key)
        {
            if (source != null && source.TryGetValue(key, out object value) && value != null)
            {
                target[key] = value;
            }
        }

        private static Dictionary<string, object> TryParseJsonObject(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            string trimmed = text.Trim();
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                int firstBreak = trimmed.IndexOf('\n');
                int lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
                if (firstBreak >= 0 && lastFence > firstBreak)
                {
                    trimmed = trimmed.Substring(firstBreak + 1, lastFence - firstBreak - 1).Trim();
                }
            }

            int start = trimmed.IndexOf('{');
            int end = trimmed.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                trimmed = trimmed.Substring(start, end - start + 1);
            }

            try
            {
                return Json.Deserialize<Dictionary<string, object>>(trimmed);
            }
            catch
            {
                return null;
            }
        }

'@

$text = $text.Substring(0, $start) + $newBlock + $text.Substring($end)

$text = $text.Replace(
    '                ["eventsModel"] = "zai-org/glm-5.2",' + "`r`n" + '                ["memoryModel"] = "zai-org/glm-5.2",',
    '                ["eventsModel"] = "zai-org/glm-5.2",' + "`r`n" + '                ["memoryModel"] = "zai-org/glm-5.2",' + "`r`n" + '                ["strategyModel"] = "zai-org/glm-5.2",' + "`r`n" + '                ["temperature"] = 0.7d,' + "`r`n" + '                ["maxTokens"] = 900,'
)

$text = $text.Replace(
    '|| name.Equals("actions.jsonl", StringComparison.OrdinalIgnoreCase))',
    '|| name.Equals("actions.jsonl", StringComparison.OrdinalIgnoreCase)' + "`r`n" + '                || name.Equals("action-queue.json", StringComparison.OrdinalIgnoreCase)' + "`r`n" + '                || name.Equals("llm-log.jsonl", StringComparison.OrdinalIgnoreCase))'
)

$text = $text.Replace(
    "<option value='actions.jsonl'>actions.jsonl</option>",
    "<option value='actions.jsonl'>actions.jsonl</option>" + "`r`n" + "              <option value='action-queue.json'>action-queue.json</option>" + "`r`n" + "              <option value='llm-log.jsonl'>llm-log.jsonl</option>"
)

$text = $text.Replace(
    'NanoGPT routing will use these settings.',
    'Any OpenAI-compatible chat-completions endpoint can use these settings. NanoGPT is the default, but the API URL box can point to another provider.'
)

$text = $text.Replace(
    'Different jobs can use different NanoGPT models later. These are saved now so the routing layer has a stable place to read from.',
    'Different jobs can use different model IDs on any OpenAI-compatible backend.'
)

$text = $text.Replace(
    "<label>App Title <input id='appTitle'></label>",
    "<label>App Title <input id='appTitle'></label>" + "`r`n" + "        <label>Temperature <input id='temperature' type='number' min='0' max='2' step='0.05'></label>" + "`r`n" + "        <label>Max Tokens <input id='maxTokens' type='number' min='64' max='32000'></label>"
)

$text = $text.Replace(
    "<label>Events <input id='eventsModel'></label>" + "`r`n" + "        <label>Memory <input id='memoryModel'></label>",
    "<label>Events <input id='eventsModel'></label>" + "`r`n" + "        <label>Memory <input id='memoryModel'></label>" + "`r`n" + "        <label>Strategy / Actions <input id='strategyModel'></label>"
)

$text = $text.Replace(
    "'dialogueModel','diplomacyModel','eventsModel','memoryModel'",
    "'dialogueModel','diplomacyModel','eventsModel','memoryModel','strategyModel','temperature','maxTokens'"
)

$text = $text.Replace(
    "const intIds = new Set(['port','maxContextMemories']);",
    "const intIds = new Set(['port','maxContextMemories','maxTokens']);"
)

$helperMarker = @'
        private static List<string> ReadStringList(Dictionary<string, object> source, string key)
'@

$helperInsert = @'
        private static double ReadDouble(Dictionary<string, object> source, string key, double fallback)
        {
            return source != null && source.TryGetValue(key, out object value) && value != null && double.TryParse(Convert.ToString(value), out double parsed) ? parsed : fallback;
        }

        private static string ReadFirstString(Dictionary<string, object> source, params string[] keys)
        {
            if (source == null || keys == null)
            {
                return string.Empty;
            }

            foreach (string key in keys)
            {
                string value = ReadString(source, key, "");
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }

        private static List<Dictionary<string, object>> ReadDictionaryList(Dictionary<string, object> source, string key)
        {
            if (source == null || !source.TryGetValue(key, out object value) || value == null)
            {
                return new List<Dictionary<string, object>>();
            }

            if (value is ArrayList array)
            {
                return array.Cast<object>().OfType<Dictionary<string, object>>().ToList();
            }

            if (value is IEnumerable<object> enumerable)
            {
                return enumerable.OfType<Dictionary<string, object>>().ToList();
            }

            Dictionary<string, object> single = value as Dictionary<string, object>;
            return single == null ? new List<Dictionary<string, object>>() : new List<Dictionary<string, object>> { single };
        }

'@

if (-not $text.Contains($helperMarker)) {
    throw 'Could not find helper insertion marker.'
}
$text = $text.Replace($helperMarker, $helperInsert + $helperMarker)

[System.IO.File]::WriteAllText($path, $text, [System.Text.UTF8Encoding]::new($false))

Select-String -LiteralPath $path -Pattern '/actions/catalog|/actions/next|/actions/decide|ChatWithLlm|ActionCatalog|strategyModel|maxTokens|llm-log' -Context 1,1
