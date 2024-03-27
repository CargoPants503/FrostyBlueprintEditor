﻿using System;
using System.ComponentModel;
using System.Linq;
using BlueprintEditorPlugin.Editors.BlueprintEditor.Connections;
using BlueprintEditorPlugin.Editors.BlueprintEditor.Nodes.Ports;
using BlueprintEditorPlugin.Editors.BlueprintEditor.NodeWrangler;
using BlueprintEditorPlugin.Editors.GraphEditor.LayoutManager.IO;
using BlueprintEditorPlugin.Editors.NodeWrangler;
using BlueprintEditorPlugin.Models.Connections;
using BlueprintEditorPlugin.Models.Nodes;
using BlueprintEditorPlugin.Models.Nodes.Ports;
using BlueprintEditorPlugin.Models.Nodes.Utilities;
using Frosty.Core;
using Frosty.Core.Controls;
using FrostySdk.Ebx;
using FrostySdk.IO;

namespace BlueprintEditorPlugin.Editors.BlueprintEditor.Nodes.Utilities
{
    public class EntityInputRedirect : BaseRedirect, IObjectContainer
    {
        public object Object { get; }

        public ConnectionType ConnectionType
        {
            get
            {
                if (Inputs.Count != 0)
                {
                    return ((EntityPort)Inputs[0]).Type;
                }
                else
                {
                    return ((EntityPort)Outputs[0]).Type;
                }
            }
        }

        public void OnObjectModified(object sender, ItemModifiedEventArgs args)
        {
            EditRedirectArgs edit = (EditRedirectArgs)Object;
            Header = string.IsNullOrEmpty(edit.Header) ? null : edit.Header;
        }

        public override bool Load(LayoutReader reader)
        {
            if (reader.ReadBoolean())
            {
                return false;
            }
            
            EntityNodeWrangler wrangler = (EntityNodeWrangler)NodeWrangler;
            
            string header = reader.ReadNullTerminatedString();
            if (header != "{None}")
            {
                Header = header;
            }

            bool isInterface = reader.ReadBoolean();
            if (isInterface)
            {
                reader.ReadInt();
                reader.ReadInt();
                ConnectionType type = (ConnectionType)reader.ReadInt();
                string portName = reader.ReadNullTerminatedString();
                InterfaceNode node = wrangler.GetInterfaceNode(portName, PortDirection.In);
                EntityInput input = node.GetInput(portName, type);

                RedirectTarget = input;
                
                Direction = PortDirection.Out;
                switch (input.Type)
                {
                    case ConnectionType.Event:
                    {
                        Outputs.Add(new EventOutput(input.Name, this)
                        {
                            HasPlayer = input.HasPlayer,
                            Realm = Realm.Any
                        });
                    } break;
                    case ConnectionType.Link:
                    {
                        Outputs.Add(new LinkOutput(input.Name, this)
                        {
                            Realm = Realm.Any
                        });
                    } break;
                    case ConnectionType.Property:
                    {
                        Outputs.Add(new PropertyOutput(input.Name, this)
                        {
                            IsInterface = input.IsInterface,
                            Realm = Realm.Any
                        });
                    } break;
                }

                Location = reader.ReadPoint();

                SourceRedirect = new EntityInputRedirect(input, PortDirection.In, NodeWrangler);
                SourceRedirect.Location = reader.ReadPoint();
                SourceRedirect.TargetRedirect = this;
                
                NodeWrangler.AddNode(SourceRedirect);
                
                input.Redirect(this);
            }
            else
            {
                PointerRefType nodeType = (PointerRefType)reader.ReadInt();
                EntityNode node;
                if (nodeType == PointerRefType.External)
                {
                    Guid fileGuid = reader.ReadGuid();
                    AssetClassGuid internalGuid = reader.ReadAssetClassGuid();
                    node = wrangler.GetEntityNode(fileGuid, internalGuid);
                }
                else
                {
                    node = wrangler.GetEntityNode(new AssetClassGuid(reader.ReadInt()));
                }
                
                ConnectionType type = (ConnectionType)reader.ReadInt();
                EntityPort port = node.GetInput(reader.ReadNullTerminatedString(), type);

                RedirectTarget = port;

                Direction = PortDirection.Out;
                switch (port.Type)
                {
                    case ConnectionType.Event:
                    {
                        Outputs.Add(new EventOutput(port.Name, this)
                        {
                            HasPlayer = port.HasPlayer,
                            Realm = Realm.Any
                        });
                    } break;
                    case ConnectionType.Link:
                    {
                        Outputs.Add(new LinkOutput(port.Name, this)
                        {
                            Realm = Realm.Any
                        });
                    } break;
                    case ConnectionType.Property:
                    {
                        Outputs.Add(new PropertyOutput(port.Name, this)
                        {
                            IsInterface = port.IsInterface,
                            Realm = Realm.Any
                        });
                    } break;
                }

                Location = reader.ReadPoint();

                RedirectTarget.PropertyChanged += RedirectTargetPropertyChanged;
                SourceRedirect = new EntityInputRedirect(port, PortDirection.In, NodeWrangler);
                SourceRedirect.Location = reader.ReadPoint();
                SourceRedirect.TargetRedirect = this;
                
                NodeWrangler.AddNode(SourceRedirect);
                
                port.Redirect(this);
            }

            return true;
        }
        
        /// <summary>
        /// FORMAT STRUCTURE:
        /// bool - Skip(informs the loader this should be skipped)
        /// NullTerminatedString - header
        /// bool - IsInterface
        /// int - NodeType
        /// guid - FileGuid(missing if NodeType internal)
        /// AssetClassGuid - internalid
        /// int - PortType
        /// NullTerminatedString - portname
        ///
        /// GENERIC VERTEX FORMAT:
        /// Point - OurPosition
        /// Point - SourcePosition
        /// </summary>
        /// <param name="writer"></param>
        public override void Save(LayoutWriter writer)
        {
            if (Direction != PortDirection.Out)
            {
                writer.Write(true);
                return;
            }
            
            IObjectNode node = (IObjectNode)RedirectTarget.Node;
            
            writer.Write(false);
            writer.WriteNullTerminatedString(Header ?? "{None}");
            
            bool isInterface = node is InterfaceNode;
            writer.Write(isInterface);
            
            writer.Write((int)node.Type);
            if (node.Type == PointerRefType.External)
            {
                writer.Write(node.FileGuid);
                writer.Write(node.InternalGuid);
            }
            else
            {
                writer.Write(node.InternalGuid.InternalId);
            }
            
            EntityPort redirectTarget = (EntityPort)RedirectTarget;
            writer.Write((int)redirectTarget.Type);
            writer.WriteNullTerminatedString(RedirectTarget.Name);
            
            writer.Write(Location);
            writer.Write(SourceRedirect.Location);
        }

        protected override void RedirectTargetPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            base.RedirectTargetPropertyChanged(sender, e);
            switch (e.PropertyName)
            {
                case "IsInterface":
                {
                    if (Direction == PortDirection.In)
                    {
                        if (((EntityPort)Inputs[0]).IsInterface == ((EntityPort)RedirectTarget).IsInterface)
                                return;
                        
                        ((EntityPort)Inputs[0]).IsInterface = ((EntityPort)RedirectTarget).IsInterface;
                    }
                } break;
                case "Realm":
                {
                    if (Direction == PortDirection.In)
                    {
                        if (((EntityPort)Inputs[0]).Realm == ((EntityPort)RedirectTarget).Realm)
                            return;
                        
                        ((EntityPort)Inputs[0]).Realm = ((EntityPort)RedirectTarget).Realm;
                    }
                } break;
            }
        }

        protected override void OurPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "IsInterface":
                {
                    if (Direction == PortDirection.In)
                    {
                        if (((EntityPort)RedirectTarget).IsInterface == ((EntityPort)Inputs[0]).IsInterface)
                            return;
                        
                        ((EntityPort)RedirectTarget).IsInterface = ((EntityPort)Inputs[0]).IsInterface;
                    }
                    else
                    {
                        if (((EntityPort)RedirectTarget).IsInterface == ((EntityPort)Outputs[0]).IsInterface)
                            return;
                        
                        ((EntityPort)RedirectTarget).IsInterface = ((EntityPort)Outputs[0]).IsInterface;
                    }
                } break;
                case "Realm":
                {
                    if (Direction == PortDirection.In)
                    {
                        if (((EntityPort)RedirectTarget).Realm == ((EntityPort)Inputs[0]).Realm)
                            return;
                        
                        ((EntityPort)RedirectTarget).Realm = ((EntityPort)Inputs[0]).Realm;
                    }
                    else
                    {
                        if (((EntityPort)RedirectTarget).Realm == ((EntityPort)Outputs[0]).Realm)
                            return;
                        
                        ((EntityPort)RedirectTarget).Realm = ((EntityPort)Outputs[0]).Realm;
                    }
                } break;
            }
        }

        public EntityInputRedirect(EntityPort redirectTarget, PortDirection direction, INodeWrangler wrangler)
        {
            RedirectTarget = redirectTarget;
            Direction = direction;
            NodeWrangler = wrangler;

            if (Direction == PortDirection.In)
            {
                switch (redirectTarget.Type)
                {
                    case ConnectionType.Event:
                    {
                        Inputs.Add(new EventInput(redirectTarget.Name, redirectTarget.Node)
                        {
                            HasPlayer = redirectTarget.HasPlayer,
                            Realm = redirectTarget.Realm
                        });
                    } break;
                    case ConnectionType.Link:
                    {
                        Inputs.Add(new LinkInput(redirectTarget.Name, redirectTarget.Node)
                        {
                            Realm = redirectTarget.Realm
                        });
                    } break;
                    case ConnectionType.Property:
                    {
                        Inputs.Add(new PropertyInput(redirectTarget.Name, redirectTarget.Node)
                        {
                            IsInterface = redirectTarget.IsInterface,
                            Realm = redirectTarget.Realm
                        });
                    } break;
                }

                Inputs[0].PropertyChanged += OurPropertyChanged;
            }
            else
            {
                switch (redirectTarget.Type)
                {
                    case ConnectionType.Event:
                    {
                        Outputs.Add(new EventOutput(redirectTarget.Name, this)
                        {
                            HasPlayer = redirectTarget.HasPlayer,
                            Realm = Realm.Any
                        });
                    } break;
                    case ConnectionType.Link:
                    {
                        Outputs.Add(new LinkOutput(redirectTarget.Name, this)
                        {
                            Realm = Realm.Any
                        });
                    } break;
                    case ConnectionType.Property:
                    {
                        Outputs.Add(new PropertyOutput(redirectTarget.Name, this)
                        {
                            IsInterface = redirectTarget.IsInterface,
                            Realm = Realm.Any
                        });
                    } break;
                }

                Outputs[0].PropertyChanged += OurPropertyChanged;
            }

            RedirectTarget.PropertyChanged += RedirectTargetPropertyChanged;
            Object = new EditRedirectArgs(this);
        }

        public EntityInputRedirect(INodeWrangler wrangler)
        {
            NodeWrangler = wrangler;
        }

        public EntityInputRedirect()
        {
        }
    }
    
    public class EntityOutputRedirect : BaseRedirect, IObjectContainer
    {
        public object Object { get; }

        public ConnectionType ConnectionType
        {
            get
            {
                if (Inputs.Count != 0)
                {
                    return ((EntityPort)Inputs[0]).Type;
                }
                else
                {
                    return ((EntityPort)Outputs[0]).Type;
                }
            }
        }

        public override void OnDestruction()
        {
            if (Direction == PortDirection.Out)
            {
                foreach (BaseConnection connection in NodeWrangler.GetConnections(Outputs[0]))
                {
                    connection.Source = RedirectTarget;
                }
            }
            else
            {
                IConnection connection = NodeWrangler.GetConnections(Inputs[0]).First();
                NodeWrangler.RemoveConnection(connection);
            }
        }

        public void OnObjectModified(object sender, ItemModifiedEventArgs args)
        {
            EditRedirectArgs edit = (EditRedirectArgs)Object;
            Header = string.IsNullOrEmpty(edit.Header) ? null : edit.Header;
        }

        public override bool Load(LayoutReader reader)
        {
            if (reader.ReadBoolean())
            {
                return false;
            }
            
            EntityNodeWrangler wrangler = (EntityNodeWrangler)NodeWrangler;
            
            string header = reader.ReadNullTerminatedString();
            if (header != "{None}")
            {
                Header = header;
            }

            bool isInterface = reader.ReadBoolean();
            if (isInterface)
            {
                reader.ReadInt();
                reader.ReadInt();
                ConnectionType type = (ConnectionType)reader.ReadInt();
                string portName = reader.ReadNullTerminatedString();
                InterfaceNode node = wrangler.GetInterfaceNode(portName, PortDirection.Out);
                EntityOutput output = node.GetOutput(portName, type);

                RedirectTarget = output;
                
                Direction = PortDirection.In;
                switch (output.Type)
                {
                    case ConnectionType.Event:
                    {
                        Inputs.Add(new EventInput(output.Name, this)
                        {
                            HasPlayer = output.HasPlayer,
                            Realm = Realm.Any
                        });
                    } break;
                    case ConnectionType.Link:
                    {
                        Inputs.Add(new LinkInput(output.Name, this)
                        {
                            Realm = Realm.Any
                        });
                    } break;
                    case ConnectionType.Property:
                    {
                        Inputs.Add(new PropertyInput(output.Name, this)
                        {
                            IsInterface = output.IsInterface,
                            Realm = Realm.Any
                        });
                    } break;
                }

                Location = reader.ReadPoint();

                SourceRedirect = new EntityOutputRedirect(output, PortDirection.Out, NodeWrangler);
                SourceRedirect.Location = reader.ReadPoint();
                SourceRedirect.TargetRedirect = this;
                
                NodeWrangler.AddNode(SourceRedirect);
                
                output.Redirect(this);
            }
            else
            {
                PointerRefType nodeType = (PointerRefType)reader.ReadInt();
                EntityNode node;
                if (nodeType == PointerRefType.External)
                {
                    Guid fileGuid = reader.ReadGuid();
                    AssetClassGuid internalGuid = reader.ReadAssetClassGuid();
                    node = wrangler.GetEntityNode(fileGuid, internalGuid);
                }
                else
                {
                    node = wrangler.GetEntityNode(new AssetClassGuid(reader.ReadInt()));
                }
                
                ConnectionType type = (ConnectionType)reader.ReadInt();
                EntityPort port = node.GetOutput(reader.ReadNullTerminatedString(), type);

                RedirectTarget = port;

                Direction = PortDirection.In;
                switch (port.Type)
                {
                    case ConnectionType.Event:
                    {
                        Inputs.Add(new EventInput(port.Name, this)
                        {
                            HasPlayer = port.HasPlayer,
                            Realm = Realm.Any
                        });
                    } break;
                    case ConnectionType.Link:
                    {
                        Inputs.Add(new LinkInput(port.Name, this)
                        {
                            Realm = Realm.Any
                        });
                    } break;
                    case ConnectionType.Property:
                    {
                        Inputs.Add(new PropertyInput(port.Name, this)
                        {
                            IsInterface = port.IsInterface,
                            Realm = Realm.Any
                        });
                    } break;
                }
                Inputs[0].PropertyChanged += OurPropertyChanged;

                Location = reader.ReadPoint();

                RedirectTarget.PropertyChanged += RedirectTargetPropertyChanged;
                SourceRedirect = new EntityOutputRedirect(port, PortDirection.Out, NodeWrangler);
                SourceRedirect.Location = reader.ReadPoint();
                SourceRedirect.TargetRedirect = this;
                
                NodeWrangler.AddNode(SourceRedirect);
                
                port.Redirect(this);
            }

            return true;
        }
        
        public override void Save(LayoutWriter writer)
        {
            if (Direction != PortDirection.In)
            {
                writer.Write(true);
                return;
            }
            
            IObjectNode node = (IObjectNode)RedirectTarget.Node;
            
            writer.Write(false);
            writer.WriteNullTerminatedString(Header ?? "{None}");
            
            bool isInterface = node is InterfaceNode;
            writer.Write(isInterface);
            
            writer.Write((int)node.Type);
            if (node.Type == PointerRefType.External)
            {
                writer.Write(node.FileGuid);
                writer.Write(node.InternalGuid);
            }
            else
            {
                writer.Write(node.InternalGuid.InternalId);
            }
            
            EntityPort redirectTarget = (EntityPort)RedirectTarget;
            writer.Write((int)redirectTarget.Type);
            writer.WriteNullTerminatedString(RedirectTarget.Name);
            
            writer.Write(Location);
            writer.Write(SourceRedirect.Location);
        }

        protected override void RedirectTargetPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "Name":
                {
                    if (Direction == PortDirection.Out)
                    {
                        Outputs[0].Name = RedirectTarget.Name;
                    }
                    else
                    {
                        Inputs[0].Name = RedirectTarget.Name;
                    }
                } break;
                case "Node":
                {
                    App.Logger.LogError("Fuck! Someone tell ywingpilot2 that the port node changed on a redirect...");
                    throw new NotImplementedException("Fuck! Someone tell ywingpilot2 that the port node changed on a redirect...");
                }
                case "IsInterface":
                {
                    if (Direction == PortDirection.Out)
                    {
                        if (((EntityPort)Outputs[0]).IsInterface == ((EntityPort)RedirectTarget).IsInterface)
                            return;
                        
                        ((EntityPort)Outputs[0]).IsInterface = ((EntityPort)RedirectTarget).IsInterface;
                    }
                } break;
                case "Realm":
                {
                    if (Direction == PortDirection.Out)
                    {
                        if (((EntityPort)Outputs[0]).Realm == ((EntityPort)RedirectTarget).Realm)
                            return;
                        
                        ((EntityPort)Outputs[0]).Realm = ((EntityPort)RedirectTarget).Realm;
                    }
                } break;
            }
        }

        protected override void OurPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "IsInterface":
                {
                    if (Direction == PortDirection.In)
                    {
                        if (((EntityPort)RedirectTarget).IsInterface == ((EntityPort)Inputs[0]).IsInterface)
                            return;
                        
                        ((EntityPort)RedirectTarget).IsInterface = ((EntityPort)Inputs[0]).IsInterface;
                    }
                    else
                    {
                        if (((EntityPort)RedirectTarget).IsInterface == ((EntityPort)Outputs[0]).IsInterface)
                            return;
                        
                        ((EntityPort)RedirectTarget).IsInterface = ((EntityPort)Outputs[0]).IsInterface;
                    }
                } break;
                case "Realm":
                {
                    if (Direction == PortDirection.In)
                    {
                        if (((EntityPort)RedirectTarget).Realm == ((EntityPort)Inputs[0]).Realm)
                            return;
                        
                        ((EntityPort)RedirectTarget).Realm = ((EntityPort)Inputs[0]).Realm;
                    }
                    else
                    {
                        if (((EntityPort)RedirectTarget).Realm == ((EntityPort)Outputs[0]).Realm)
                            return;
                        
                        ((EntityPort)RedirectTarget).Realm = ((EntityPort)Outputs[0]).Realm;
                    }
                } break;
            }
        }

        public EntityOutputRedirect(EntityPort redirectTarget, PortDirection direction, INodeWrangler wrangler)
        {
            RedirectTarget = redirectTarget;
            Direction = direction;
            NodeWrangler = wrangler;

            if (Direction == PortDirection.In)
            {
                switch (redirectTarget.Type)
                {
                    case ConnectionType.Event:
                    {
                        Inputs.Add(new EventInput(redirectTarget.Name, this)
                        {
                            HasPlayer = redirectTarget.HasPlayer,
                            Realm = Realm.Any
                        });
                    } break;
                    case ConnectionType.Link:
                    {
                        Inputs.Add(new LinkInput(redirectTarget.Name, this)
                        {
                            Realm = Realm.Any
                        });
                    } break;
                    case ConnectionType.Property:
                    {
                        Inputs.Add(new PropertyInput(redirectTarget.Name, this)
                        {
                            IsInterface = redirectTarget.IsInterface,
                            Realm = Realm.Any
                        });
                    } break;
                }
                Inputs[0].PropertyChanged += OurPropertyChanged;
            }
            else
            {
                switch (redirectTarget.Type)
                {
                    case ConnectionType.Event:
                    {
                        Outputs.Add(new EventOutput(redirectTarget.Name, redirectTarget.Node)
                        {
                            HasPlayer = redirectTarget.HasPlayer,
                            Realm = redirectTarget.Realm
                        });
                    } break;
                    case ConnectionType.Link:
                    {
                        Outputs.Add(new LinkOutput(redirectTarget.Name, redirectTarget.Node)
                        {
                            Realm = redirectTarget.Realm
                        });
                    } break;
                    case ConnectionType.Property:
                    {
                        Outputs.Add(new PropertyOutput(redirectTarget.Name, redirectTarget.Node)
                        {
                            IsInterface = redirectTarget.IsInterface,
                            Realm = redirectTarget.Realm
                        });
                    } break;
                }
                Outputs[0].PropertyChanged += OurPropertyChanged;
            }

            RedirectTarget.PropertyChanged += RedirectTargetPropertyChanged;
            Object = new EditRedirectArgs(this);
        }

        public EntityOutputRedirect(INodeWrangler wrangler)
        {
            NodeWrangler = wrangler;
        }

        public EntityOutputRedirect()
        {
        }
    }
    
    public class EditRedirectArgs
    {
        public string Header { get; set; }

        public EditRedirectArgs(BaseRedirect redirect)
        {
            if (redirect.Header == null)
            {
                Header = "";
                return;
            }
            
            Header = redirect.Header;
        }

        public EditRedirectArgs()
        {
        }
    }
}