using DfE.CheckPerformanceData.Web.Admin.Rules;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Admin.Rules;

public sealed class BranchEditTransformsTests
{
    // root AllOf(1) -> [ FieldEq(2), AnyOf(3) -> [ FieldEq(4) ] ]
    private static List<PredicateNodeForm> Tree() => new()
    {
        new() { Id = 1, ParentId = null, Kind = PredicateKind.AllOf },
        new() { Id = 2, ParentId = 1, Kind = PredicateKind.FieldEq, Field = "keyStage", Operator = "eq", Value = "KS4" },
        new() { Id = 3, ParentId = 1, Kind = PredicateKind.AnyOf },
        new() { Id = 4, ParentId = 3, Kind = PredicateKind.FieldEq, Field = "keyStage", Operator = "eq", Value = "KS2" },
    };

    [Fact]
    public void NextId_Is_Max_Plus_One()
    {
        Assert.Equal(5, BranchEditTransforms.NextId(Tree()));
        Assert.Equal(1, BranchEditTransforms.NextId(new List<PredicateNodeForm>()));
    }

    [Fact]
    public void AddCondition_Appends_Leaf_Under_Parent()
    {
        var list = Tree();
        BranchEditTransforms.AddCondition(list, parentId: 1);
        var added = list.Single(n => n.Id == 5);
        Assert.Equal(1, added.ParentId);
        Assert.Equal(PredicateKind.FieldEq, added.Kind);
        Assert.Equal("eq", added.Operator);
    }

    [Fact]
    public void AddGroup_Appends_Empty_AllOf_Under_Parent()
    {
        var list = Tree();
        BranchEditTransforms.AddGroup(list, parentId: 3);
        var added = list.Single(n => n.Id == 5);
        Assert.Equal(3, added.ParentId);
        Assert.Equal(PredicateKind.AllOf, added.Kind);
    }

    [Fact]
    public void Remove_Drops_Node_And_All_Descendants()
    {
        var list = Tree();
        BranchEditTransforms.Remove(list, id: 3);
        Assert.DoesNotContain(list, n => n.Id is 3 or 4);
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public void Ungroup_Reparents_Children_And_Deletes_Composite()
    {
        var list = Tree();
        BranchEditTransforms.Ungroup(list, compositeId: 3);
        Assert.DoesNotContain(list, n => n.Id == 3);
        Assert.Equal(1, list.Single(n => n.Id == 4).ParentId);
    }

    [Fact]
    public void SetField_Resets_Operator_And_Value_When_Type_Changes()
    {
        var list = Tree();
        // The model binder has already applied the user's new field selection onto the node;
        // SetField must honour it, not revert to a stale value.
        list.Single(n => n.Id == 2).Field = "pupilAge";
        BranchEditTransforms.SetField(list, id: 2);
        var node = list.Single(n => n.Id == 2);
        Assert.Equal("pupilAge", node.Field);
        Assert.Equal("eq", node.Operator);
        Assert.Equal("", node.Value);
    }

    [Fact]
    public void SetField_Keeps_Bound_Field_And_Does_Not_Revert()
    {
        // Regression guard: the old SetField overwrote the bound field with a render-time arg,
        // reverting the user's choice. The field the user picked must survive.
        var list = Tree();
        list.Single(n => n.Id == 2).Field = "pupilAge"; // user changed keyStage -> pupilAge
        BranchEditTransforms.SetField(list, id: 2);
        Assert.Equal("pupilAge", list.Single(n => n.Id == 2).Field);
    }

    [Fact]
    public void SetField_To_Type_Without_Current_Operator_Picks_First_Valid()
    {
        var list = Tree();
        list.Single(n => n.Id == 2).Operator = "in";
        list.Single(n => n.Id == 2).Field = "isAddBack";
        BranchEditTransforms.SetField(list, id: 2);
        var node = list.Single(n => n.Id == 2);
        Assert.Equal("eq", node.Operator);
    }

    [Fact]
    public void AddValue_And_RemoveValue_Mutate_FieldIn_List()
    {
        var list = new List<PredicateNodeForm>
        {
            new() { Id = 1, Kind = PredicateKind.FieldIn, Field = "inclusionFlag", Operator = "in",
                    Values = new List<string> { "A" } }
        };
        BranchEditTransforms.AddValue(list, id: 1);
        Assert.Equal(2, list[0].Values.Count);
        BranchEditTransforms.RemoveValue(list, id: 1, index: 0);
        Assert.Single(list[0].Values);
    }

    [Fact]
    public void Collapse_Sets_Flag_On_Group()
    {
        var list = Tree();
        BranchEditTransforms.Collapse(list, id: 3); // the AnyOf group
        Assert.True(list.Single(n => n.Id == 3).Collapsed);
    }

    [Fact]
    public void Collapse_Ignores_A_Leaf()
    {
        var list = Tree();
        BranchEditTransforms.Collapse(list, id: 2); // a FieldEq leaf
        Assert.False(list.Single(n => n.Id == 2).Collapsed);
    }

    [Fact]
    public void Expand_Clears_Flag()
    {
        var list = Tree();
        list.Single(n => n.Id == 3).Collapsed = true;
        BranchEditTransforms.Expand(list, id: 3);
        Assert.False(list.Single(n => n.Id == 3).Collapsed);
    }

    [Fact]
    public void Remove_Terminates_On_Tampered_ParentId_Cycle()
    {
        // node 2 and node 3 reference each other (crafted cycle); removing node 2 must not hang.
        var list = new List<PredicateNodeForm>
        {
            new() { Id = 1, ParentId = null, Kind = PredicateKind.AllOf },
            new() { Id = 2, ParentId = 1, Kind = PredicateKind.AllOf },
            new() { Id = 3, ParentId = 2, Kind = PredicateKind.AllOf },
        };
        list.Single(n => n.Id == 2).ParentId = 3; // 2 -> 3 -> 2 cycle

        BranchEditTransforms.Remove(list, id: 2);

        Assert.DoesNotContain(list, n => n.Id == 2);
    }
}
