using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Media.Media3D;
using log4net;
using Xbim.Common;
using Xbim.Common.Federation;
using Xbim.Common.Geometry;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;

namespace Xbim.Presentation.LayerStyling
{
    public class SurfaceLayerStyler : ILayerStyler, IProgressiveLayerStyler
    {
        protected static readonly ILog Log = LogManager.GetLogger("Xbim.Presentation.LayerStyling.SurfaceLayerStyler");

        public event ProgressChangedEventHandler ProgressChanged;

        // ReSharper disable once CollectionNeverUpdated.Local
        readonly XbimColourMap _colourMap = new XbimColourMap();

        public Dictionary<int, HashSet<WpfMeshGeometry3D>> MeshesByModel = new Dictionary<int, HashSet<WpfMeshGeometry3D>>();//model id-> meshes set; // complex mesh to use in selection
        public Dictionary<int, Dictionary<int, HashSet<WpfMeshGeometry3D>>> ModelMeshesByStyle = new Dictionary<int, Dictionary<int, HashSet<WpfMeshGeometry3D>>>();//model id-> geometries(style, mesh set); //in view
        public Dictionary<int, Dictionary<int, HashSet<int>>> ModelStyleByProduct = new Dictionary<int, Dictionary<int, HashSet<int>>>(); //model -> (product, style)

        /// <summary>
        /// This version uses the new Geometry representation
        /// </summary>
        /// <param name="model"></param>
        /// <param name="modelTransform">The transform to place the models geometry in the right place</param>
        /// <param name="opaqueShapes"></param>
        /// <param name="transparentShapes"></param>
        /// <param name="exclude">List of type to exclude, by default excplict openings and spaces are excluded if exclude = null</param>
        /// <returns></returns>
        public XbimScene<WpfMeshGeometry3D, WpfMaterial> BuildScene(IModel model, XbimMatrix3D modelTransform, ModelVisual3D opaqueShapes, ModelVisual3D transparentShapes,
            List<Type> exclude = null)
        {
            var excludedTypes = model.DefaultExclusions(exclude);

            var scene = new XbimScene<WpfMeshGeometry3D, WpfMaterial>(model);
            var timer = new Stopwatch();
            timer.Start();
            using (var geomStore = model.GeometryStore)
            {
                using (var geomReader = geomStore.BeginRead())
                {
                    var tmpOpaquesGroup = new Model3DGroup();
                    var tmpTransparentsGroup = new Model3DGroup();

                    var materialsByStyleId = new Dictionary<int, WpfMaterial>();
                    var styleByProduct = new Dictionary<int, HashSet<int>>();
                    var meshesSetByStyleId = new Dictionary<int, HashSet<WpfMeshGeometry3D>>();

                    //get a list of all the unique style ids then build their style and mesh
                    var sstyleIds = geomReader.StyleIds;
                    foreach (var styleId in sstyleIds)
                    {
                        var wpfMaterial = GetWpfMaterial(model, styleId);
                        materialsByStyleId.Add(styleId, wpfMaterial);

                        var mg = GetNewStyleMesh(wpfMaterial, tmpTransparentsGroup, tmpOpaquesGroup);

                        HashSet<WpfMeshGeometry3D> tmpHashSet;
                        if (!meshesSetByStyleId.TryGetValue(styleId, out tmpHashSet))
                        {
                            tmpHashSet = new HashSet<WpfMeshGeometry3D>();
                            meshesSetByStyleId.Add(styleId, tmpHashSet);
                        }
                        tmpHashSet.Add(mg);
                    }
                    var modelMeshes = new HashSet<WpfMeshGeometry3D>();
                    //Add first empty mesh to modelMeshes
                    var modelMesh = new WpfMeshGeometry3D();
                    modelMesh.WpfModel.SetValue(FrameworkElement.TagProperty, modelMesh);
                    modelMesh.BeginUpdate();
                    modelMeshes.Add(modelMesh);
                    //

                    var tot = 1;
                    if (ProgressChanged != null)
                    {
                        // only enumerate if there's a need for progress update
                        tot = geomReader.ShapeInstances.Count();
                    }
                    var prog = 0;
                    var lastProgress = 0;

                    foreach (var shapeInstance in geomReader.ShapeInstances)
                    {
                        // logging 
                        if (ProgressChanged != null)
                        {
                            var currentProgress = 100 * prog++ / tot;
                            if (currentProgress != lastProgress)
                            {
                                ProgressChanged(this, new ProgressChangedEventArgs(currentProgress, "Creating visuals"));
                                lastProgress = currentProgress;
                            }
                        }

                        IXbimShapeGeometryData shapeGeom = geomReader.ShapeGeometry(shapeInstance.ShapeGeometryLabel);
                        if (shapeGeom.Format != (byte)XbimGeometryType.PolyhedronBinary)
                            continue;

                        var transform = XbimMatrix3D.Multiply(shapeInstance.Transformation, modelTransform);

                        bool isExclude = excludedTypes.Contains(shapeInstance.IfcTypeId);
                        if (!isExclude && shapeInstance.RepresentationType == XbimGeometryRepresentationType.OpeningsAndAdditionsIncluded)
                        {
                            //to render cache and selection cache

                            // work out style
                            var styleId = shapeInstance.StyleLabel > 0
                                ? shapeInstance.StyleLabel
                                : shapeInstance.IfcTypeId * -1;
                            
                            if (!materialsByStyleId.ContainsKey(styleId))
                            {
                                // if the style is not available we build one by ExpressType
                                var material = GetWpfMaterialByType(model, shapeInstance.IfcTypeId);
                                materialsByStyleId.Add(styleId, material);

                                var mg = GetNewStyleMesh(material, tmpTransparentsGroup, tmpOpaquesGroup);

                                HashSet<WpfMeshGeometry3D> tmpHashSet;
                                if (!meshesSetByStyleId.TryGetValue(styleId, out tmpHashSet))
                                {
                                    tmpHashSet = new HashSet<WpfMeshGeometry3D>();
                                    meshesSetByStyleId.Add(styleId, tmpHashSet);
                                }
                                tmpHashSet.Add(mg);
                            }

                            //GET THE ACTUAL GEOMETRY

                            #region For Render
                            {
                                //merge last mesh  (we combine meshes to one(or more) big for fast rendering)
                                var targetMergeMeshByStyle = meshesSetByStyleId[styleId].Last();

                                // replace target mesh beyond suggested size 
                                // https://docs.microsoft.com/en-us/dotnet/framework/wpf/graphics-multimedia/maximize-wpf-3d-performance
                                // 
                                // if very big - create new mesh
                                if (targetMergeMeshByStyle.PositionCount > 20000
                                    ||
                                    targetMergeMeshByStyle.TriangleIndexCount > 60000)
                                {
                                    targetMergeMeshByStyle.EndUpdate();
                                    var newTargetMergeMeshByStyle = GetNewStyleMesh(materialsByStyleId[styleId], tmpTransparentsGroup, tmpOpaquesGroup);

                                    HashSet<WpfMeshGeometry3D> tmpHashSet;
                                    if (!meshesSetByStyleId.TryGetValue(styleId, out tmpHashSet))
                                    {
                                        tmpHashSet = new HashSet<WpfMeshGeometry3D>();
                                        meshesSetByStyleId.Add(styleId, tmpHashSet);
                                    }
                                    tmpHashSet.Add(newTargetMergeMeshByStyle);
                                    targetMergeMeshByStyle = newTargetMergeMeshByStyle;
                                }
                                // end replace                                                                       
                                targetMergeMeshByStyle.Add(
                                    shapeGeom.ShapeData,
                                    shapeInstance.IfcTypeId,
                                    shapeInstance.IfcProductLabel,
                                    shapeInstance.InstanceLabel, transform,
                                    (short)model.UserDefinedId);

                                HashSet<int> productStyleSet;
                                if (!styleByProduct.TryGetValue(shapeInstance.IfcProductLabel, out productStyleSet))
                                {
                                    productStyleSet = new HashSet<int>();
                                    styleByProduct.Add(shapeInstance.IfcProductLabel, productStyleSet);
                                }
                                productStyleSet.Add(styleId);
                            }
                            #endregion
                            #region For Selection                            
                            // get big meshes of model for fast future selection
                            {
                                //get last
                                var targetModelMesh = modelMeshes.Last(); ;

                                if (targetModelMesh.PositionCount > 20000
                                   ||
                                   targetModelMesh.TriangleIndexCount > 60000)
                                {
                                    targetModelMesh.EndUpdate();

                                    var newTargetModelMesh = new WpfMeshGeometry3D();
                                    newTargetModelMesh.WpfModel.SetValue(FrameworkElement.TagProperty, newTargetModelMesh);
                                    newTargetModelMesh.BeginUpdate();
                                    modelMeshes.Add(newTargetModelMesh);

                                    targetModelMesh = newTargetModelMesh;
                                }

                                targetModelMesh.Add(
                                    shapeGeom.ShapeData,
                                    shapeInstance.IfcTypeId,
                                    shapeInstance.IfcProductLabel,
                                    shapeInstance.InstanceLabel, transform,
                                    (short)model.UserDefinedId);
                            }
                            #endregion
                        }
                        if (isExclude)
                        {
                            //only to selection cache

                            var targetModelMesh = modelMeshes.Last(); ;

                            if (targetModelMesh.PositionCount > 20000
                               ||
                               targetModelMesh.TriangleIndexCount > 60000)
                            {
                                targetModelMesh.EndUpdate();

                                var newTargetModelMesh = new WpfMeshGeometry3D();
                                newTargetModelMesh.WpfModel.SetValue(FrameworkElement.TagProperty, newTargetModelMesh);
                                newTargetModelMesh.BeginUpdate();
                                modelMeshes.Add(newTargetModelMesh);

                                targetModelMesh = newTargetModelMesh;
                            }

                            targetModelMesh.Add(
                                shapeGeom.ShapeData,
                                shapeInstance.IfcTypeId,
                                shapeInstance.IfcProductLabel,
                                shapeInstance.InstanceLabel, transform,
                                (short)model.UserDefinedId);

                        }
                    }

                    if (ModelStyleByProduct.ContainsKey(model.UserDefinedId))
                        ModelStyleByProduct[model.UserDefinedId] = styleByProduct;
                    else
                        ModelStyleByProduct.Add(model.UserDefinedId, styleByProduct);

                    foreach (var wpfMeshGeometry3DSet in meshesSetByStyleId.Values)
                    {
                        wpfMeshGeometry3DSet.Last().EndUpdate();
                    }

                    if (ModelMeshesByStyle.ContainsKey(model.UserDefinedId))
                        ModelMeshesByStyle[model.UserDefinedId] = meshesSetByStyleId;
                    else
                        ModelMeshesByStyle.Add(model.UserDefinedId, meshesSetByStyleId);

                    modelMeshes.Last().EndUpdate();
                    if (MeshesByModel.ContainsKey(model.UserDefinedId))
                        MeshesByModel[model.UserDefinedId] = modelMeshes;
                    else
                        MeshesByModel.Add(model.UserDefinedId, modelMeshes);                   

                    if (tmpOpaquesGroup.Children.Any())
                    {
                        var mv = new ModelVisual3D { Content = tmpOpaquesGroup };
                        opaqueShapes.Children.Add(mv);
                    }
                    if (tmpTransparentsGroup.Children.Any())
                    {
                        var mv = new ModelVisual3D { Content = tmpTransparentsGroup };
                        transparentShapes.Children.Add(mv);
                    }
                }
            }
            Log.DebugFormat("Time to load visual components: {0:F3} seconds", timer.Elapsed.TotalSeconds);

            ProgressChanged?.Invoke(this, new ProgressChangedEventArgs(0, "Ready"));
            return scene;
        }

        protected IEnumerable<XbimShapeInstance> GetShapeInstancesToRender(IGeometryStoreReader geomReader, HashSet<short> excludedTypes)
        {
            var shapeInstances = geomReader.ShapeInstances
                .Where(s => s.RepresentationType == XbimGeometryRepresentationType.OpeningsAndAdditionsIncluded
                            &&
                            !excludedTypes.Contains(s.IfcTypeId));
            return shapeInstances;
        }


        protected static WpfMeshGeometry3D GetNewStyleMesh(WpfMaterial wpfMaterial, Model3DGroup tmpTransparentsGroup,
            Model3DGroup tmpOpaquesGroup)
        {
            var mg = new WpfMeshGeometry3D(wpfMaterial, wpfMaterial);
            
            mg.WpfModel.SetValue(FrameworkElement.TagProperty, mg);
            mg.BeginUpdate();
            if (wpfMaterial.IsTransparent)
                tmpTransparentsGroup.Children.Add(mg);
            else
                tmpOpaquesGroup.Children.Add(mg);
            return mg;
        }

        protected static WpfMaterial GetWpfMaterial(IModel model, int styleId)
        {
            var sStyle = model.Instances[styleId] as IIfcSurfaceStyle;
            var texture = XbimTexture.Create(sStyle);
            texture.DefinedObjectId = styleId;
            var wpfMaterial = new WpfMaterial();
            wpfMaterial.CreateMaterial(texture);
            return wpfMaterial;
        }

        protected WpfMaterial GetWpfMaterialByType(IModel model, short typeid)
        {
            var prodType = model.Metadata.ExpressType(typeid);
            var v = _colourMap[prodType.Name];
            var texture = XbimTexture.Create(v);
            var material2 = new WpfMaterial();
            material2.CreateMaterial(texture);
            return material2;
        }


        public void SetFederationEnvironment(IReferencedModel refModel)
        {
            
        }
    }
}
