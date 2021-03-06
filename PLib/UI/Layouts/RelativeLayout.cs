﻿/*
 * Copyright 2020 Peter Han
 * Permission is hereby granted, free of charge, to any person obtaining a copy of this software
 * and associated documentation files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use, copy, modify, merge, publish,
 * distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all copies or
 * substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING
 * BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
 * DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
 */

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using PeterHan.PLib.UI.Layouts;

using RelativeLayoutData = ListPool<PeterHan.PLib.UI.Layouts.RelativeLayoutResults,
	PeterHan.PLib.UI.RelativeLayout>;

namespace PeterHan.PLib.UI {
	/// <summary>
	/// A class that lays out raw game objects relative to each other, creating a final layout
	/// that depends only on the Unity anchor primitives. Creates the highest performance
	/// layouts of any layout manager, equivalent to those hand designed in Unity, but has
	/// limited flexibility for adapting to changes in size.
	/// 
	/// Objects must be added to the supplied game object manually in addition to adding their
	/// constraints in this layout manager. Objects which lack constraints for any edge will
	/// have that edge automatically constrained to the edge of the parent object, including
	/// any insets.
	/// </summary>
	public sealed class RelativeLayout {
		/// <summary>
		/// The parent game object where the layout will be performed.
		/// </summary>
		internal GameObject Parent { get; }

		/// <summary>
		/// The margin added around all components in the layout. This is in addition to any
		/// margins around the components.
		/// 
		/// Note that this margin is not taken into account with percentage based anchors.
		/// Items anchored to the extremes will always work fine. Items anchored in the middle
		/// will use the middle <b>before</b> margins are effective.
		/// </summary>
		public RectOffset OverallMargin { get; set; }

		/// <summary>
		/// Constraints for each object are stored here.
		/// </summary>
		private readonly IDictionary<GameObject, RelativeLayoutParams> locConstraints;

		/// <summary>
		/// Creates a new relative layout. This class is not a layout group as it does not
		/// remain attached to the parent post execution.
		/// </summary>
		/// <param name="parent">The object to lay out.</param>
		public RelativeLayout(GameObject parent) {
			OverallMargin = null;
			Parent = parent ?? throw new ArgumentNullException("parent");
			locConstraints = new Dictionary<GameObject, RelativeLayoutParams>(32);
		}

		/// <summary>
		/// Retrieves the parameters for a child game object. Creates an entry if none exists
		/// for this component.
		/// </summary>
		/// <param name="item">The item to look up.</param>
		/// <returns>The parameters for that object.</returns>
		private RelativeLayoutParams AddOrGet(GameObject item) {
			if (!locConstraints.TryGetValue(item, out RelativeLayoutParams param))
				locConstraints[item] = param = new RelativeLayoutParams();
			return param;
		}

		/// <summary>
		/// Anchors the component's pivot in the X axis to the specified anchor position.
		/// The component will be laid out at its preferred (or overridden) width with its
		/// pivot locked to the specified relative fraction of the parent component's width.
		/// 
		/// Any other existing left or right edge constraints will be overwritten. This method
		/// is equivalent to setting both the left and right edges to the same fraction.
		/// </summary>
		/// <param name="item">The component to adjust.</param>
		/// <param name="anchor">The fraction to which to align the pivot, with 0.0f
		/// being the left and 1.0f being the right.</param>
		/// <returns>This object, for call chaining.</returns>
		public RelativeLayout AnchorXAxis(GameObject item, float anchor = 0.5f) {
			SetLeftEdge(item, fraction: anchor);
			return SetRightEdge(item, fraction: anchor);
		}

		/// <summary>
		/// Anchors the component's pivot in the Y axis to the specified anchor position.
		/// The component will be laid out at its preferred (or overridden) height with its
		/// pivot locked to the specified relative fraction of the parent component's height.
		/// 
		/// Any other existing top or bottom edge constraints will be overwritten. This method
		/// is equivalent to setting both the top and bottom edges to the same fraction.
		/// </summary>
		/// <param name="item">The component to adjust.</param>
		/// <param name="anchor">The fraction to which to align the pivot, with 0.0f
		/// being the bottom and 1.0f being the top.</param>
		/// <returns>This object, for call chaining.</returns>
		public RelativeLayout AnchorYAxis(GameObject item, float anchor = 0.5f) {
			SetTopEdge(item, fraction: anchor);
			return SetBottomEdge(item, fraction: anchor);
		}

		/// <summary>
		/// Executes the relative layout with the current constraints and children. The
		/// objects will be arranged using Unity anchors which allows the layout to adapt to
		/// changes in size without rebuilding or invoking auto-layout again, increasing
		/// performance greatly over layout managers.
		/// </summary>
		/// <param name="addLayoutElement">If true, adds a LayoutElement to the parent
		/// indicating its preferred and minimum size. Even if the resulting layout could be
		/// expanded, its flexible size will always default to zero, although it can be changed
		/// after construction using SetFlexUISize.</param>
		/// <exception cref="InvalidOperationException">If the layout constraints cannot
		/// be successfully resolved to final positions - for example, if components depend
		/// on each other in a cycle.</exception>
		/// <returns>The parent game object.</returns>
		public GameObject Execute(bool addLayoutElement = false) {
			if (Parent == null)
				throw new InvalidOperationException("Parent was disposed");
			var all = Parent.rectTransform();
			var children = RelativeLayoutData.Allocate();
			var components = ListPool<ILayoutController, RelativeLayout>.Allocate();
			// Calculate overall margins
			int ml = OverallMargin?.left ?? 0, mr = OverallMargin?.right ?? 0,
				mt = OverallMargin?.top ?? 0, mb = OverallMargin?.bottom ?? 0;
			// Gather the children!
			children.CalcX(all, locConstraints);
			int passes, limit = 2 * children.Count;
			// X layout
			for (passes = 0; passes < limit && !children.RunPassX(); passes++) ;
			if (passes >= limit)
				children.ThrowUnresolvable(passes, PanelDirection.Horizontal);
			float minX = children.GetMinSizeX() + ml + mr;
			all.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, minX);
			children.ExecuteX(components, ml, mr);
			// Y layout
			children.CalcY();
			for (passes = 0; passes < limit && !children.RunPassY(); passes++) ;
			if (passes >= limit)
				children.ThrowUnresolvable(passes, PanelDirection.Vertical);
			float minY = children.GetMinSizeY() + mb + mt;
			all.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, minY);
			children.ExecuteY(components, mb, mt);
			components.Recycle();
			children.Recycle();
			if (addLayoutElement)
				Parent.SetUISize(new Vector2(minX, minY), true);
			return Parent;
		}

		/// <summary>
		/// Overrides the preferred size of a component. If set, instead of looking at layout
		/// sizes of the component, the specified size will be used instead.
		/// </summary>
		/// <param name="item">The component to adjust.</param>
		/// <param name="size">The size to apply. Only dimensions greater than zero will be used.</param>
		/// <returns>This object, for call chaining.</returns>
		public RelativeLayout OverrideSize(GameObject item, Vector2 size) {
			if (item != null)
				AddOrGet(item).OverrideSize = size;
			return this;
		}

		/// <summary>
		/// Sets the bottom edge of a game object. If the fraction is supplied, the component
		/// will be laid out with the bottom edge anchored to that fraction of the parent's
		/// height. If a component is specified and no fraction is specified, the component
		/// will be anchored with its bottom edge above the top edge of that component.
		/// If neither is specified, all bottom edge constraints will be removed.
		/// 
		/// Any other existing bottom edge constraint will be overwritten.
		/// 
		/// Remember that +Y is in the upwards direction.
		/// </summary>
		/// <param name="item">The component to adjust.</param>
		/// <param name="fraction">The fraction to which to align the bottom edge, with 0.0f
		/// being the bottom and 1.0f being the top.</param>
		/// <param name="above">The game object which this component must be above.</param>
		/// <returns>This object, for call chaining.</returns>
		public RelativeLayout SetBottomEdge(GameObject item, float fraction = -1.0f,
				GameObject above = null) {
			if (item != null) {
				if (above == item)
					throw new ArgumentException("Component cannot refer directly to itself");
				SetEdge(AddOrGet(item).BottomEdge, fraction, above);
			}
			return this;
		}

		/// <summary>
		/// Sets a component's edge constraint.
		/// </summary>
		/// <param name="edge">The edge to set.</param>
		/// <param name="fraction">The fraction of the parent to anchor.</param>
		/// <param name="child">The other component to anchor.</param>
		private void SetEdge(RelativeLayoutParams.EdgeStatus edge, float fraction,
				GameObject child) {
			if (fraction >= 0.0f && fraction <= 1.0f) {
				edge.Constraint = RelativeConstraintType.ToAnchor;
				edge.FromAnchor = fraction;
				edge.FromComponent = null;
			} else if (child != null) {
				edge.Constraint = RelativeConstraintType.ToComponent;
				edge.FromComponent = child;
			} else {
				edge.Constraint = RelativeConstraintType.Unconstrained;
				edge.FromComponent = null;
			}
		}

		/// <summary>
		/// Sets the left edge of a game object. If the fraction is supplied, the component
		/// will be laid out with the left edge anchored to that fraction of the parent's
		/// width. If a component is specified and no fraction is specified, the component
		/// will be anchored with its left edge to the right of that component.
		/// If neither is specified, all left edge constraints will be removed.
		/// 
		/// Any other existing left edge constraint will be overwritten.
		/// </summary>
		/// <param name="item">The component to adjust.</param>
		/// <param name="fraction">The fraction to which to align the left edge, with 0.0f
		/// being the left and 1.0f being the right.</param>
		/// <param name="toLeft">The game object which this component must be to the right of.</param>
		/// <returns>This object, for call chaining.</returns>
		public RelativeLayout SetLeftEdge(GameObject item, float fraction = -1.0f,
				GameObject toRight = null) {
			if (item != null) {
				if (toRight == item)
					throw new ArgumentException("Component cannot refer directly to itself");
				SetEdge(AddOrGet(item).LeftEdge, fraction, toRight);
			}
			return this;
		}

		/// <summary>
		/// Sets the insets of a component from its anchor points. A positive number insets the
		/// component away from the edge, whereas a negative number out-sets the component
		/// across the edge.
		/// 
		/// All components default to no insets.
		/// 
		/// Any reference to a component's edge using other constraints always refers to its
		/// edge <b>before</b> insets are applied.
		/// </summary>
		/// <param name="item">The component to adjust.</param>
		/// <param name="insets">The insets to apply. If null, the insets will be set to zero.</param>
		/// <returns>This object, for call chaining.</returns>
		public RelativeLayout SetMargin(GameObject item, RectOffset insets) {
			if (item != null)
				AddOrGet(item).Insets = insets;
			return this;
		}

		/// <summary>
		/// Sets all layout parameters of an object at once.
		/// </summary>
		/// <param name="item">The item to configure.</param>
		/// <param name="rawParams">The raw parameters to use.</param>
		internal void SetRaw(GameObject item, RelativeLayoutParams rawParams) {
			if (item != null && rawParams != null)
				locConstraints[item] = rawParams;
		}

		/// <summary>
		/// Sets the right edge of a game object. If the fraction is supplied, the component
		/// will be laid out with the right edge anchored to that fraction of the parent's
		/// width. If a component is specified and no fraction is specified, the component
		/// will be anchored with its right edge to the left of that component.
		/// If neither is specified, all right edge constraints will be removed.
		/// 
		/// Any other existing right edge constraint will be overwritten.
		/// </summary>
		/// <param name="item">The component to adjust.</param>
		/// <param name="fraction">The fraction to which to align the right edge, with 0.0f
		/// being the left and 1.0f being the right.</param>
		/// <param name="toLeft">The game object which this component must be to the left of.</param>
		/// <returns>This object, for call chaining.</returns>
		public RelativeLayout SetRightEdge(GameObject item, float fraction = -1.0f,
				GameObject toLeft = null) {
			if (item != null) {
				if (toLeft == item)
					throw new ArgumentException("Component cannot refer directly to itself");
				SetEdge(AddOrGet(item).RightEdge, fraction, toLeft);
			}
			return this;
		}

		/// <summary>
		/// Sets the top edge of a game object. If the fraction is supplied, the component
		/// will be laid out with the top edge anchored to that fraction of the parent's
		/// height. If a component is specified and no fraction is specified, the component
		/// will be anchored with its top edge above the bottom edge of that component.
		/// If neither is specified, all top edge constraints will be removed.
		/// 
		/// Any other existing top edge constraint will be overwritten.
		/// 
		/// Remember that +Y is in the upwards direction.
		/// </summary>
		/// <param name="item">The component to adjust.</param>
		/// <param name="fraction">The fraction to which to align the top edge, with 0.0f
		/// being the bottom and 1.0f being the top.</param>
		/// <param name="below">The game object which this component must be below.</param>
		/// <returns>This object, for call chaining.</returns>
		public RelativeLayout SetTopEdge(GameObject item, float fraction = -1.0f,
				GameObject below = null) {
			if (item != null) {
				if (below == item)
					throw new ArgumentException("Component cannot refer directly to itself");
				SetEdge(AddOrGet(item).TopEdge, fraction, below);
			}
			return this;
		}
	}
}
