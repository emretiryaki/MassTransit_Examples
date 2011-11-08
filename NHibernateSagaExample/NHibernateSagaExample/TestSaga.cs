using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MassTransit.Saga;
using Magnum.StateMachine;
using MassTransit;
using FluentNHibernate.Mapping;
using MassTransit.NHibernateIntegration;
using System.Threading;

namespace NHibernateSagaExample
{
    public class MessageA 
    {
        public Guid CorrelationId { get; set; }
    };
    
    public class MessageB 
    {
        public Guid CorrelationId { get; set; }
        public int Number { get; set; }
    };

    public class ExampleSaga : SagaStateMachine<ExampleSaga>, ISaga
    {
        static ExampleSaga()
        {
            Define(() =>
                {
                    Correlate(ReceivedMessageA).By((saga, message) => saga.CorrelationId == message.CorrelationId);
                    Correlate(ReceivedMessageB).By((saga, message) => saga.CorrelationId == message.CorrelationId);

                    Initially(
                        When(ReceivedMessageA)
                        .Then((saga, message) => saga.Consume(message))
                        .TransitionTo(Waiting)
                        );

                    During(Waiting,
                        When(ReceivedMessageB)
                        .Then((saga, message) => saga.Consume(message))
                        );

                    During(Waiting, When(AllMessagesReceived)
                        .Then((saga) => saga.Finished())
                        .Complete()
                        );
                });
        }

        // Uniqueness
        public Guid CorrelationId { get; set; }

        // States.
        public static State Initial { get; set; }
        public static State Waiting { get; set; }
        public static State Completed { get; set; }

        // Events.
        public static Event<MessageA> ReceivedMessageA { get; set; }
        public static Event<MessageB> ReceivedMessageB { get; set; }
        public static Event AllMessagesReceived { get; set; }

        // Direct Injected objects
        public IServiceBus Bus { get; set; }

        // Saga State
        public int MessagesReceived { get; set; }
        public int MessagesSent { get; set; }


        public ExampleSaga()
        {
            CorrelationId = Magnum.CombGuid.Generate();
            MessagesReceived = 0;
        }

        public ExampleSaga(Guid correlationId)
        {
            CorrelationId = correlationId;
            MessagesReceived = 0;
        }

        // Deal with the Messages that come in.
        public void Consume(MessageA message)
        {
            Console.WriteLine("Received MessageA");
            MessagesSent = 2000;
            for (int i = 0; i < MessagesSent; ++i)
            {
                Bus.Publish(new MessageB { CorrelationId = CorrelationId, Number = i });
            }
        }

        public void Consume(MessageB message)
        {
            //Console.WriteLine("Received MessageB {0}", message.Number);
            ++MessagesReceived;
            if (MessagesReceived == MessagesSent)
            {
                RaiseEvent(ExampleSaga.AllMessagesReceived);
            }
        }

        public void Finished()
        {
            Console.WriteLine("Saga complete.");
        }
    }

    public class ExampleSagaMap : ClassMap<ExampleSaga>
    {
        public ExampleSagaMap()
        {
            Not.LazyLoad();

            Id(x => x.CorrelationId).GeneratedBy.Assigned();

            Map(x => x.CurrentState)
                .Access.ReadOnlyPropertyThroughCamelCaseField(Prefix.Underscore)
                .CustomType<StateMachineUserType>();

            Map(x => x.MessagesReceived);
            Map(x => x.MessagesSent);
        }
    }
}
