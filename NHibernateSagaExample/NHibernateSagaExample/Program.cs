using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using log4net.Config;
using System.IO;
using Castle.Windsor;
using log4net.Core;
using MassTransit;
using Castle.Windsor.Installer;
using Castle.MicroKernel.Registration;
using FluentDateTime;
using FluentNHibernate.Cfg;
using FluentNHibernate.Cfg.Db;
using System.Data;
using NHibernate;
using NHibernate.Tool.hbm2ddl;
using MassTransit.Saga;
using MassTransit.NHibernateIntegration.Saga;
using System.Threading;

namespace NHibernateSagaExample
{
    /// <summary>
    /// This is an example program to demonstrate how to get a NHibernate backed Saga working.
    /// </summary>
    class Program
    {
        static void Main(string[] args)
        {
            IWindsorContainer container;
            IServiceBus bus;
            ISessionFactory sessionFactory;

            XmlConfigurator.Configure(new FileInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log4net.config")));

            // Setup Castle Windor.
            container = new WindsorContainer();

            // Setup and Start MassTransit.
            container.Register(
                AllTypes.FromThisAssembly().BasedOn<IConsumer>().Configure(c => c.LifeStyle.Transient),

                // Setup a factory to build the IServiceBus.
                Component.For<IServiceBus>().UsingFactoryMethod(() =>
                    {
                        return ServiceBusFactory.New(sbc =>
                        {
                            sbc.UseRabbitMq();
                            sbc.UseRabbitMqRouting();
                            sbc.ReceiveFrom("rabbitmq://localhost/NHibernateSagaExample");
                            sbc.SetConcurrentConsumerLimit(100);
                            sbc.UseControlBus();
                            sbc.SetCreateMissingQueues(true);
                            sbc.SetDefaultTransactionTimeout(5.Minutes());
                            sbc.SetPurgeOnStartup(true);
                            sbc.Subscribe(subs =>
                            {
                                subs.LoadFrom(container);
                            });
                        });
                    }).LifeStyle.Singleton,

                // Setup a facotry to build a ISessionFactory.
                Component.For<ISessionFactory>().UsingFactoryMethod(() =>
                    {
                        return Fluently.Configure()
                            .Database(
                                MsSqlConfiguration.MsSql2005
                                    .AdoNetBatchSize(100)
                                    .ConnectionString(s => s.Is("Server=(local);initial catalog=test;Trusted_Connection=yes"))
                                    .DefaultSchema("dbo")
                                    //.ShowSql()
                                    .Raw(NHibernate.Cfg.Environment.Isolation, IsolationLevel.Serializable.ToString()))
                            .ProxyFactoryFactory("NHibernate.Bytecode.DefaultProxyFactoryFactory, NHibernate")
                            .Mappings(m =>
                            {
                                // Tell Fluent NHibernate about our mapping to store the Saga.
                                m.FluentMappings.Add<ExampleSagaMap>();
                            })
                            .ExposeConfiguration(c => new SchemaUpdate(c).Execute(false, true))
                            .BuildSessionFactory();
                    }).LifeStyle.Singleton,

                // This is where we tell Windsor to provide a NHibernate saga repostory.
                Component.For<ISagaRepository<ExampleSaga>>()
                    //.ImplementedBy<InMemorySagaRepository<ExampleSaga>>()
                    .ImplementedBy<NHibernateSagaRepository<ExampleSaga>>()
                    .LifeStyle.Singleton
                );

            // Cause NHibernate to initialise.
            sessionFactory = container.Resolve<ISessionFactory>();

            // Cause the bus to initialise.
            bus = container.Resolve<IServiceBus>();

            // Register the Saga.
            bus.SubscribeSaga(container.Resolve<ISagaRepository<ExampleSaga>>());

            // Send two messages to the Saga.  Two sagas should be creatd to handle these messages.
            bus.Publish(new MessageA { CorrelationId = Magnum.CombGuid.Generate() });
            bus.Publish(new MessageA { CorrelationId = Magnum.CombGuid.Generate() });

            // Wait for the User to hit enter.
            Console.WriteLine("Press Enter to close this applicaiton.");
            Console.ReadLine();

            // Shut it all down.
            bus.Dispose();
            container.Dispose();
        }
    }
}
