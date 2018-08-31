﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DevExpress.Data.Filtering;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Actions;
using DevExpress.ExpressApp.Editors;
using DevExpress.ExpressApp.Layout;
using DevExpress.ExpressApp.Model.NodeGenerators;
using DevExpress.ExpressApp.SystemModule;
using DevExpress.ExpressApp.Templates;
using DevExpress.ExpressApp.Utils;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.Validation;
using CIIP.Module.BusinessObjects;
using System.Diagnostics;
using System.Drawing;
using CIIP.Module.BusinessObjects.SYS;
using 常用基类;
using DevExpress.ExpressApp.Model;

namespace CIIP.Module.Controllers
{
    // For more typical usage scenarios, be sure to check out https://documentation.devexpress.com/eXpressAppFramework/clsDevExpressExpressAppWindowControllertopic.aspx.
    public partial class DataInitializeWindowController : WindowController
    {
        public DataInitializeWindowController()
        {
            InitializeComponent();
            TargetWindowType = WindowType.Main;
            // Target required Windows (via the TargetXXX properties) and create their Actions.
        }
        protected override void OnActivated()
        {
            base.OnActivated();
            // Perform various tasks depending on the target Window.
        }
        protected override void OnDeactivated()
        {
            // Unsubscribe from previously subscribed events and release other references and resources.
            base.OnDeactivated();
        }

        private void 数据初始化_Execute(object sender, SimpleActionExecuteEventArgs e)
        {
            var os = Application.CreateObjectSpace();
            CreateSystemTypes(os,Application.Model,true);
            os.CommitChanges();
        }

        public static void CreateSystemTypes(IObjectSpace ObjectSpace,IModelApplication app,bool deleteExists)
        {
            var objs = ObjectSpace.GetObjects<BusinessObject>(new BinaryOperator("IsRuntimeDefine", false));
            if (objs.Count > 0)
                return;

            ObjectSpace.Delete(objs);

            #region 系统类型
            AddBusinessObject(typeof (单据<>), "单据<明细>", CreateNameSpace(typeof (单据<>).Namespace,ObjectSpace),
                "单据<明细>,继承自单据基类,支持将明细类型做为参数传入(内部使用泛型实现)", false,ObjectSpace);

            AddBusinessObject(typeof (明细<>), "明细<单据>", CreateNameSpace(typeof (明细<>).Namespace, ObjectSpace),
                "可以与单据<明细>成对继承使用.", false, ObjectSpace);
            //第一步,创建出所有基类类型

            var exists = ObjectSpace.GetObjects<BusinessObjectBase>(null, true);

            foreach (var bom in app.BOModel)
            {
                if (!bom.TypeInfo.Type.IsGenericType && bom.TypeInfo.Type.Assembly.Location != AdmiralEnvironment.UserDefineBusinessFile.FullName)
                {
                    var ns = CreateNameSpace(bom.TypeInfo.Type.Namespace, ObjectSpace);
                    AddBusinessObject(bom.TypeInfo.Type, bom.Caption, ns, "", false, ObjectSpace);
                }
            }

            //第二步,创建这些类的属性,因为属性中可能使用了类型,所以先要创建类型.
            var bos = ObjectSpace.GetObjects<BusinessObject>(null, true);
            foreach (var bob in bos.Where(x => !x.IsRuntimeDefine))
            {
                var type = ReflectionHelper.FindType(bob.FullName);

                if (type.BaseType != null && type.BaseType != typeof (object))
                {
                    var fullName = type.BaseType.Namespace + "." + type.BaseType.Name;

                    var baseType = bos.SingleOrDefault(x => x.FullName == fullName);
                    //bob.Base = baseType;

                    //if (bob.Base == null)
                    //{
                    //    Debug.WriteLine(type.FullName + "没有找到基类:" + fullName);
                    //}
                }

                if (type.IsGenericType)
                {
                    //bob.DisableCreateGenericParameterValues = true;

                    foreach (var item in type.GetGenericArguments())
                    {
                        var gp = ObjectSpace.CreateObject<GenericParameterInstance>();
                        //gp.Owner = bob;
                        gp.Name = item.Name;
                        if (item.IsGenericParameter)
                        {
                            gp.ParameterIndex = item.GenericParameterPosition;
                        }
                        else
                        {
                        }
                        if (!string.IsNullOrEmpty(item.FullName))
                            gp.ParameterValue = exists.SingleOrDefault(x => x.FullName == item.FullName);

                        var att =
                            item.GetCustomAttributes(typeof (ItemTypeAttribute), false)
                                .OfType<ItemTypeAttribute>()
                                .FirstOrDefault();
                        if (att != null)
                        {
                            var gi = bos.SingleOrDefault(x => x.FullName == att.ItemType.FullName);
                            gp.DefaultGenericType = gi;
                        }
                    }
                }


                var typeInfo = CaptionHelper.ApplicationModel.BOModel.GetClass(type);
                if (typeInfo != null && false)
                {
                    foreach (var tim in typeInfo.OwnMembers)
                    {
                        if (tim.MemberInfo.IsAssociation)
                        {
                            var collectionMember = ObjectSpace.CreateObject<CollectionProperty>();
                            collectionMember.BusinessObject = bob;

                            collectionMember.Aggregated = tim.MemberInfo.IsAggregated;
                            collectionMember.Name = tim.Name;
                            collectionMember.PropertyType = bos.SingleOrDefault(x => x.FullName == tim.Type.FullName);
                            if (collectionMember.PropertyType == null)
                            {
                                Debug.WriteLine("没有找到属性类型:" + tim.Type.FullName);
                                //throw new Exception("没有找到属性类型" + tim.Type.FullName);
                            }
                        }
                        else
                        {
                            var member = ObjectSpace.CreateObject<Property>();
                            member.BusinessObject = bob;
                            member.Name = tim.Name;
                            member.PropertyType = exists.SingleOrDefault(x => x.FullName == tim.Type.FullName);
                            member.BusinessObject = bob;
                            if (member.PropertyType == null)
                            {
                                Debug.WriteLine("没有找到属性类型:" + tim.Type.FullName);
                                //throw new Exception("没有找到属性类型" + tim.Type.FullName);
                            }
                        }
                    }
                }
                else
                {
                    Debug.WriteLine("没有找到类型:" + type.FullName);
                }
            }

            //第三步,设置属性的关联属性,因为可能用到第二步中创建的属性,及,创建关系.
            foreach (var bob in bos.Where(x => !x.IsRuntimeDefine))
            {
                var cps = bob.Properties.OfType<CollectionProperty>();
                if (bob.Properties.Count > 0)
                {
                    var type = ReflectionHelper.FindType(bob.FullName);
                    var typeInfo = CaptionHelper.ApplicationModel.BOModel.GetClass(type);
                    foreach (var cp in cps)
                    {
                        var mi = typeInfo.FindMember(cp.Name);
                        cp.RelationProperty = cp.PropertyType.FindProperty(mi.MemberInfo.AssociatedMemberInfo.Name);
                    }
                }
            }

            #endregion

            ObjectSpace.CommitChanges();
        }

        public static Namespace CreateNameSpace(string ns,IObjectSpace ObjectSpace)
        {
            var find = ObjectSpace.FindObject<Namespace>(new BinaryOperator("FullName", ns));

            if (find == null)
            {
                var part = ns.Split('.');
                if (part.Length > 0)
                {
                    find = ObjectSpace.CreateObject<Namespace>();
                    find.Name = part[part.Length - 1];
                    if (part.Length > 1)
                    {
                        find.Parent = CreateNameSpace(string.Join(".", part.Take(part.Length - 1)), ObjectSpace);
                    }
                }
            }
            return find;
        }
        public static BusinessObject AddBusinessObject(Type type, string caption, Namespace nameSpace, string description, bool isRuntimeDefine,IObjectSpace ObjectSpace)
        {
            var t = ObjectSpace.FindObject<BusinessObject>(new BinaryOperator("FullName", type.FullName),true);
            if (t == null)
            {
                t = ObjectSpace.CreateObject<BusinessObject>();
                //t.DisableCreateGenericParameterValues = true;
                t.Category = nameSpace;
                t.Name = type.Name;
                t.Caption = caption;
                t.Description = description;
                t.FullName = type.FullName;
                //t.CanCustomLogic = typeof(ICustomLogic).IsAssignableFrom(type);
                
                t.IsRuntimeDefine = isRuntimeDefine;
            }
            return t;
        }


    }
}
