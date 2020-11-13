/* This content is licensed under the terms of the Creative Commons Attribution 4.0 International License.
 * When using this content, you must:  bow down before them
 * •    Acknowledge that the content is from the Sansar Knowledge Base.
 * •    Include our copyright notice: "© 2017 Linden Research, Inc."
 * •    Indicate that the content is licensed under the Creative Commons Attribution-Share Alike 4.0 International License.
 * •    Include the URL for, or link to, the license summary at https://creativecommons.org/licenses/by-sa/4.0/deed.hi (and, if possible, to the complete license terms at https://creativecommons.org/licenses/by-sa/4.0/legalcode.
 * For example:
 * "This work uses content from the Sansar Knowledge Base. © 2017 Linden Research, Inc. Licensed under the Creative Commons Attribution 4.0 International License (license summary available at https://creativecommons.org/licenses/by/4.0/ and complete license terms available at https://creativecommons.org/licenses/by/4.0/legalcode)."
 * SimpleProximityDetector.cs created by Sansar User Gindipple 8/27/2018
 */

using Sansar.Script;
using Sansar.Simulation;
using Sansar;
using System;
using System.Linq;

namespace SimpleScripts
{
    public class DetectAndTeleport : SceneObjectScript
    {
        #region EditorProperties
        // The events to send on proximity detection of this object. Can be a comma separated list of event names.
        // The events sent will cycle through only the On Detection events with values set.

        // Enable responding to events for this script
        [DefaultValue("Detect")]
        [DisplayName("Detect Event --> ")]
        public readonly string DetectEvent;

        [DefaultValue("AvatarsFound")]
        [DisplayName("Avatars Detected ->")]
        public readonly string OnDetection;

        [DefaultValue("NoneFound")]
        [DisplayName("No One Detected ->")]
        public readonly string NoDetection;

        // How close to get before detected
        [DefaultValue(1.0)]
        [DisplayName("Detection Range")]
        public float DetectionRange = 1.0f;

        // The destination within the scene to teleport to
        [DisplayName("Destination")]
        [DefaultValue("<0,0,0>")]
        public readonly Vector Destination;

        #endregion

        #region SimpleHelpers v2
        // Update the region tag above by incrementing the version when updating anything in the region.

        // If a Group is set, will only respond and send to other SimpleScripts with the same Group tag set.
        // Does NOT accept CSV lists of groups.
        // To send or receive events to/from a specific group from outside that group prepend the group name with a > to the event name
        // my_group>on
        [DefaultValue("")]
        [DisplayName("Group")]
        public string Group = "";

        public interface ISimpleData
        {
            AgentInfo AgentInfo { get; }
            ObjectId ObjectId { get; }
            ObjectId SourceObjectId { get; }

            // Extra data
            Reflective ExtraData { get; }
        }

        public class SimpleData : Reflective, ISimpleData
        {
            public SimpleData(ScriptBase script) { ExtraData = script; }
            public AgentInfo AgentInfo { get; set; }
            public ObjectId ObjectId { get; set; }
            public ObjectId SourceObjectId { get; set; }

            public Reflective ExtraData { get; }
        }

        public interface IDebugger { bool DebugSimple { get; } }
        private bool __debugInitialized = false;
        private bool __SimpleDebugging = false;
        private string __SimpleTag = "";

        private string GenerateEventName(string eventName)
        {
            eventName = eventName.Trim();
            if (eventName.EndsWith("@"))
            {
                // Special case on@ to send the event globally (the null group) by sending w/o the @.
                return eventName.Substring(0, eventName.Length - 1);
            }
            else if (Group == "" || eventName.Contains("@"))
            {
                // No group was set or already targeting a specific group as is.
                return eventName;
            }
            else
            {
                // Append the group
                return $"{eventName}@{Group}";
            }
        }

        private void SetupSimple()
        {
            __debugInitialized = true;
            __SimpleTag = GetType().Name + " [S:" + Script.ID.ToString() + " O:" + ObjectPrivate.ObjectId.ToString() + "]";
            Wait(TimeSpan.FromSeconds(1));
            IDebugger debugger = ScenePrivate.FindReflective<IDebugger>("SimpleDebugger").FirstOrDefault();
            if (debugger != null) __SimpleDebugging = debugger.DebugSimple;
        }

        System.Collections.Generic.Dictionary<string, Func<string, Action<ScriptEventData>, Action>> __subscribeActions = new System.Collections.Generic.Dictionary<string, Func<string, Action<ScriptEventData>, Action>>();
        private Action SubscribeToAll(string csv, Action<ScriptEventData> callback)
        {
            if (!__debugInitialized) SetupSimple();
            if (string.IsNullOrWhiteSpace(csv)) return null;

            Func<string, Action<ScriptEventData>, Action> subscribeAction;
            if (__subscribeActions.TryGetValue(csv, out subscribeAction))
            {
                return subscribeAction(csv, callback);
            }

            // Simple case.
            if (!csv.Contains(">>"))
            {
                __subscribeActions[csv] = SubscribeToAllInternal;
                return SubscribeToAllInternal(csv, callback);
            }

            // Chaining
            __subscribeActions[csv] = (_csv, _callback) =>
            {
                System.Collections.Generic.List<string> chainedCommands = new System.Collections.Generic.List<string>(csv.Split(new string[] { ">>" }, StringSplitOptions.RemoveEmptyEntries));

                string initial = chainedCommands[0];
                chainedCommands.RemoveAt(0);
                chainedCommands.Add(initial);

                Action unsub = null;
                Action<ScriptEventData> wrappedCallback = null;
                wrappedCallback = (data) =>
                {
                    string first = chainedCommands[0];
                    chainedCommands.RemoveAt(0);
                    chainedCommands.Add(first);
                    if (unsub != null) unsub();
                    unsub = SubscribeToAllInternal(first, wrappedCallback);
                    Log.Write(LogLevel.Info, "CHAIN Subscribing to " + first);
                    _callback(data);
                };

                unsub = SubscribeToAllInternal(initial, wrappedCallback);
                return unsub;
            };

            return __subscribeActions[csv](csv, callback);
        }

        private Action SubscribeToAllInternal(string csv, Action<ScriptEventData> callback)
        {
            Action unsubscribes = null;
            string[] events = csv.Trim().Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (__SimpleDebugging)
            {
                Log.Write(LogLevel.Info, __SimpleTag, "Subscribing to " + events.Length + " events: " + string.Join(", ", events));
            }
            Action<ScriptEventData> wrappedCallback = callback;

            foreach (string eventName in events)
            {
                if (__SimpleDebugging)
                {
                    var sub = SubscribeToScriptEvent(GenerateEventName(eventName), (ScriptEventData data) =>
                    {
                        Log.Write(LogLevel.Info, __SimpleTag, "Received event " + GenerateEventName(eventName));
                        wrappedCallback(data);
                    });
                    unsubscribes += sub.Unsubscribe;
                }
                else
                {
                    var sub = SubscribeToScriptEvent(GenerateEventName(eventName), wrappedCallback);
                    unsubscribes += sub.Unsubscribe;
                }
            }
            return unsubscribes;
        }

        System.Collections.Generic.Dictionary<string, Action<string, Reflective>> __sendActions = new System.Collections.Generic.Dictionary<string, Action<string, Reflective>>();
        private void SendToAll(string csv, Reflective data)
        {
            if (!__debugInitialized) SetupSimple();
            if (string.IsNullOrWhiteSpace(csv)) return;

            Action<string, Reflective> sendAction;
            if (__sendActions.TryGetValue(csv, out sendAction))
            {
                sendAction(csv, data);
                return;
            }

            // Simple case.
            if (!csv.Contains(">>"))
            {
                __sendActions[csv] = SendToAllInternal;
                SendToAllInternal(csv, data);
                return;
            }

            // Chaining
            System.Collections.Generic.List<string> chainedCommands = new System.Collections.Generic.List<string>(csv.Split(new string[] { ">>" }, StringSplitOptions.RemoveEmptyEntries));
            __sendActions[csv] = (_csv, _data) =>
            {
                string first = chainedCommands[0];
                chainedCommands.RemoveAt(0);
                chainedCommands.Add(first);

                Log.Write(LogLevel.Info, "CHAIN Sending to " + first);
                SendToAllInternal(first, _data);
            };
            __sendActions[csv](csv, data);
        }

        private void SendToAllInternal(string csv, Reflective data)
        {
            if (string.IsNullOrWhiteSpace(csv)) return;
            string[] events = csv.Trim().Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            if (__SimpleDebugging) Log.Write(LogLevel.Info, __SimpleTag, "Sending " + events.Length + " events: " + string.Join(", ", events) + (Group != "" ? (" to group " + Group) : ""));
            foreach (string eventName in events)
            {
                PostScriptEvent(GenerateEventName(eventName), data);
            }
        }
        #endregion

        #region Private Variables

        bool AgentInDetectionRange = false;
        int PeopleInRange = 0;


        #endregion

        public override void Init()
        {
            Script.UnhandledException += UncaughtExceptionHandler;
            Log.Write("Running DetectAndTeleportPeople");
            if (DetectionRange < 0.0)
                DetectionRange = 1.0f;

            SubscribeToAll(DetectEvent, Detect);
        }

        public void Detect(ScriptEventData data)
        {
            try
            {
                PeopleInRange = 0;
                AgentInDetectionRange = false;
                foreach (AgentPrivate agent in ScenePrivate.GetAgents())
                {
                    float agentDist = (ScenePrivate.FindObject(agent.AgentInfo.ObjectId).Position - ObjectPrivate.Position).Length();
                    Log.Write("Agent: " + agent.AgentInfo.Name);
                    Log.Write("agentDist: " + agentDist);
                    Log.Write("DetectionRange: " + DetectionRange);
                    if (agentDist <= DetectionRange)
                    {
                        SimpleData sd = new SimpleData(this);
                        Log.Write("A");
                        sd.AgentInfo = agent.AgentInfo;
                        Log.Write("B");
                        sd.ObjectId = agent.AgentInfo.ObjectId;
                        Log.Write("C");
                        sd.SourceObjectId = ObjectPrivate.ObjectId;
                        Log.Write("D");
                        AgentInDetectionRange = true;
                        Log.Write("E");
                        SendToAll(OnDetection, sd);
                        Log.Write("In Range");
                        PeopleInRange++;
                            ObjectPrivate objectPrivate = ScenePrivate.FindObject(agent.AgentInfo.ObjectId);
                        if (objectPrivate != null)
                        {
                            AnimationComponent anim = null;
                            if (objectPrivate.TryGetFirstComponent(out anim))
                            {
                                anim.SetPosition(Destination);
                            }
                        }
                    }
                }
                if (PeopleInRange == 0)
                {
 
                }
            }
            catch (Exception ex)
            {
                if (__SimpleDebugging)
                {
                    Log.Write(LogLevel.Error, __SimpleTag, "Proximity Detection Failed: " + ex.Message);
                }
            }
        }

        private void UncaughtExceptionHandler(object sender, Exception ex)
        {
            string type;
            if (!Script.UnhandledExceptionRecoverable)
                type = " Urecoverable Exception: ";
            else
                type = " Exception: ";

            Log.Write(LogLevel.Error, GetType().Name + type + ex.Message);
        }
    }
}
