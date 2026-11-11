using Moq;
using SolidWorks.Interop.sldworks;
using SW2URDF.URDFExport;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SW2URDF.Test
{
    /// <summary>
    /// Showing or hiding a sub-assembly has to reach the parts below it, otherwise the mesh of a link
    /// that selects a sub-assembly is empty. Does not need SolidWorks.
    /// </summary>
    public class TestSubAssemblyVisibility
    {
        private static Mock<Component2> Component(string name, params Mock<Component2>[] children)
        {
            var component = new Mock<Component2>();
            component.Setup(c => c.Name2).Returns(name);
            component.Setup(c => c.GetChildren())
                .Returns(children.Length == 0 ? null : children.Select(c => (object)c.Object).ToArray());
            component.Setup(c => c.Select4(It.IsAny<bool>(), It.IsAny<SelectData>(), It.IsAny<bool>())).Returns(true);
            return component;
        }

        [Fact]
        public void LeafPartIsNotExpanded()
        {
            var leaf = Component("leaf-1");
            List<Component2> expanded = CommonSwOperations.ExpandSubAssemblies(
                new List<Component2> { leaf.Object });
            Assert.Equal(new[] { "leaf-1" }, expanded.Select(c => c.Name2).ToArray());
        }

        [Fact]
        public void NestedSubAssembliesAreExpandedToTheLeaves()
        {
            var inner = Component("inner-1", Component("a-1"), Component("b-1"));
            var outer = Component("outer-1", inner, Component("c-1"));
            List<Component2> expanded = CommonSwOperations.ExpandSubAssemblies(
                new List<Component2> { outer.Object, Component("d-1").Object });
            Assert.Equal(
                new[] { "outer-1", "inner-1", "a-1", "b-1", "c-1", "d-1" },
                expanded.Select(c => c.Name2).ToArray());
        }

        [Fact]
        public void ShowAndHideSelectEveryPartOfASubAssembly()
        {
            var a = Component("a-1");
            var b = Component("b-1");
            var sub = Component("sub-1", a, b);
            var selectionManager = new Mock<SelectionMgr>();
            selectionManager.Setup(m => m.CreateSelectData()).Returns(new Mock<SelectData>().Object);
            var model = new Mock<ModelDoc2>();
            model.Setup(m => m.SelectionManager).Returns(selectionManager.Object);

            CommonSwOperations.ShowComponents(model.Object, new List<Component2> { sub.Object });
            CommonSwOperations.HideComponents(model.Object, new List<Component2> { sub.Object });

            foreach (var component in new[] { sub, a, b })
            {
                component.Verify(c => c.Select4(true, It.IsAny<SelectData>(), false), Times.Exactly(2));
            }
            model.Verify(m => m.ShowComponent2(), Times.Once);
            model.Verify(m => m.HideComponent2(), Times.Once);
        }
    }
}
