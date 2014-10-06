// --------------------------------------------------------------------------------------------------------------------
// <copyright file="DistributedEventBrokerBugRepro.cs" company="Appccelerate">
//   Copyright (c) 2008-2014
//
//   Licensed under the Apache License, Version 2.0 (the "License");
//   you may not use this file except in compliance with the License.
//   You may obtain a copy of the License at
//
//       http://www.apache.org/licenses/LICENSE-2.0
//
//   Unless required by applicable law or agreed to in writing, software
//   distributed under the License is distributed on an "AS IS" BASIS,
//   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//   See the License for the specific language governing permissions and
//   limitations under the License.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

namespace Appccelerate.DistributedEventBroker
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    using Appccelerate.DistributedEventBroker.Handlers;
    using Appccelerate.DistributedEventBroker.Messages;
    using Appccelerate.EventBroker;
    using Appccelerate.EventBroker.Handlers;
    using Appccelerate.MappingEventBroker;
    using Appccelerate.MappingEventBroker.Conventions;

    using FluentAssertions;

    using Xunit;

    public class DistributedEventBrokerBugRepro
    {
        private readonly EventBroker testee;

        public DistributedEventBrokerBugRepro()
        {
            this.testee = new EventBroker();
        }

        [Fact]
        public void AddDistributedExtension_MustNotThrow()
        {
            var extension = new FakeDistributedEventBrokerExtension(ex => { });

            this.testee.Invoking(x => x.AddDistributedExtension(extension))
                .ShouldNotThrow();
        }

        [Fact]
        public void PublishingTwoEventsInParallel_MustNotThrow()
        {
            Exception exception = null;
            var distributedExtension = new FakeDistributedEventBrokerExtension(ex => exception = ex);
            this.testee.AddDistributedExtension(distributedExtension);

            var mappingExtension = new FakeMappingEventBrokerExtension();
            this.testee.AddMappingExtension(mappingExtension);

            var publisher1 = new Publisher();
            var publisher2 = new Publisher();

            var allEventsHandled = new CountdownEvent(2);
            var subscriber = new Subscriber(() => allEventsHandled.Signal());

            this.testee.Register(publisher1);
            this.testee.Register(publisher2);
            this.testee.Register(subscriber);

            publisher1.FireEvent();
            publisher2.FireEvent();

            allEventsHandled.Wait();

            exception.Should().BeNull();
        }

        private class FakeDistributedEventBrokerExtension : DistributedEventBrokerExtensionBase
        {
            public FakeDistributedEventBrokerExtension(Action<Exception> exceptionHandler)
                : base("DistributedEventBroker", new FakeEventBrokerBus(exceptionHandler))
            {
            }

            private class FakeEventBrokerBus : IEventBrokerBus
            {
                private readonly FakeEventFiredHandler handler;

                public FakeEventBrokerBus(Action<Exception> exceptionHandler)
                {
                    this.handler = new FakeEventFiredHandler(exceptionHandler);
                }

                public void Publish(IEventFired message)
                {
                    Task.Factory.StartNew(() => this.handler.Handle(message));
                }

                private class FakeEventFiredHandler : EventFiredHandlerBase
                {
                    private readonly Action<Exception> exceptionHandler;

                    public FakeEventFiredHandler(Action<Exception> exceptionHandler)
                    {
                        this.exceptionHandler = exceptionHandler;
                    }

                    public void Handle(IEventFired message)
                    {
                        try
                        {
                            this.DoHandle(message);
                        }
                        catch (Exception ex)
                        {
                            this.exceptionHandler(ex);
                        }
                    }
                }
            }
        }

        private class FakeMappingEventBrokerExtension : MappingEventBrokerExtension
        {
            public FakeMappingEventBrokerExtension()
                : base(new FakeMapper(), new FuncTopicConvention(topicInfo => true, topicName => "Local"), new FakeDestinationEventArgsTypeProvider())
            {
            }

            private class FakeMapper : IMapper
            {
                public EventArgs Map(Type sourceEventArgsType, Type destinationEventArgsType, EventArgs eventArgs)
                {
                    return eventArgs;
                }
            }

            private class FakeDestinationEventArgsTypeProvider : IDestinationEventArgsTypeProvider
            {
                public Type GetDestinationEventArgsType(string destinationTopic, Type sourceEventArgsType)
                {
                    return sourceEventArgsType;
                }
            }
        }

        private class Publisher
        {
            [EventPublication("Remote", HandlerRestriction.Asynchronous)]
            public event EventHandler Event;

            public void FireEvent()
            {
                this.Event(this, EventArgs.Empty);
            }
        }

        private class Subscriber
        {
            private readonly Action handler;

            public Subscriber(Action handler)
            {
                this.handler = handler;
            }

            [EventSubscription("Local", typeof(OnBackground))]
            public void HandleEvent(object sender, EventArgs e)
            {
                this.handler();
            }
        }
    }
}