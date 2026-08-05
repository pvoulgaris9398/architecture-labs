import React, { useState } from 'react';
import { Database, Zap, RefreshCw, ShoppingCart, CheckCircle } from 'lucide-react';

interface ProductData {
  id: number;
  name: string;
  price: number;
}

interface ProductResponse {
  dataSource: 'Redis Cache' | 'PostgreSQL Database';
  data: ProductData;
  stockInfo: {
    available: number;
    inStock: boolean;
  };
}

export default function App() {
  const [productId, setProductId] = useState<string>('1');
  const [loadingProduct, setLoadingProduct] = useState<boolean>(false);
  const [productResult, setProductResult] = useState<ProductResponse | null>(null);
  
  const [checkoutQuantity, setCheckoutQuantity] = useState<number>(1);
  const [submittingOrder, setSubmittingOrder] = useState<boolean>(false);
  const [orderMessage, setOrderMessage] = useState<string | null>(null);

  const API_BASE = 'http://localhost:5242'; // Match your local .NET API Port

  // 1. Fetch Product with Cache-Aside logic
  const handleFetchProduct = async () => {
    if (!productId) return;
    setLoadingProduct(true);
    try {
      const res = await fetch(`${API_BASE}/products/${productId}`);
      if (!res.ok) throw new Error('Product not found or system offline');
      const data: ProductResponse = await res.json();
      setProductResult(data);
    } catch (err) {
      console.error(err);
      alert('Error communicating with API Gateway.');
    } finally {
      setLoadingProduct(false);
    }
  };

  // 2. Submit Async Order via RabbitMQ
  const handleCheckout = async (e: React.FormEvent) => {
    e.preventDefault();
    setSubmittingOrder(true);
    setOrderMessage(null);
    try {
      const res = await fetch(`${API_BASE}/checkout`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          ProductId: parseInt(productId) || 1,
          Quantity: checkoutQuantity,
        }),
      });
      if (res.status === 202) {
        const data = await res.json();
        setOrderMessage(`Success! ${data.status}. Order ID: ${data.orderId}`);
      } else {
        throw new Error('Failed to submit checkout task');
      }
    } catch (err) {
      console.error(err);
      setOrderMessage('Error submitting background job.');
    } finally {
      setSubmittingOrder(false);
    }
  };

  return (
    <div style={{ fontFamily: 'system-ui, sans-serif', padding: '2rem', backgroundColor: '#f3f4f6', minHeight: '100vh', color: '#1f2937' }}>
      <header style={{ borderBottom: '2px solid #e5e7eb', paddingBottom: '1rem', marginBottom: '2rem' }}>
        <h1 style={{ margin: 0, fontSize: '2rem', color: '#111827' }}>Distributed System Sandbox</h1>
        <p style={{ color: '#4b5563', margin: '0.25rem 0 0 0' }}>TypeScript UI ➔ .NET Core Gateway ➔ Redis/Postgres & gRPC Python Worker</p>
      </header>

      <main style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(350px, 1fr))', gap: '2rem' }}>
        
        {/* PANEL 1: CACHE-ASIDE READ PATHWAY */}
        <section style={{ backgroundColor: '#ffffff', borderRadius: '8px', padding: '1.5rem', boxShadow: '0 1px 3px rgba(0,0,0,0.1)' }}>
          <h2 style={{ fontSize: '1.25rem', marginTop: 0, display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
            <RefreshCw size={20} color="rgb(59, 130, 246)" /> Synchronous Queries
          </h2>
          <p style={{ color: '#6b7280', fontSize: '0.875rem' }}>Fetches details using .NET Cache-Aside (Redis ➔ Postgres) and calls Python via gRPC for live warehouse stock lookup.</p>
          
          <div style={{ display: 'flex', gap: '0.5rem', margin: '1.5rem 0' }}>
            <input 
              type="number" 
              value={productId} 
              onChange={(e) => setProductId(e.target.value)}
              placeholder="Product ID (e.g. 1)" 
              style={{ padding: '0.5rem', border: '1px solid #d1d5db', borderRadius: '4px', width: '80px' }}
            />
            <button 
              onClick={handleFetchProduct}
              disabled={loadingProduct}
              style={{ backgroundColor: '#3b82f6', color: '#fff', border: 'none', padding: '0.5rem 1rem', borderRadius: '4px', cursor: 'pointer', fontWeight: 600 }}
            >
              {loadingProduct ? 'Fetching...' : 'Query Details'}
            </button>
          </div>

          {productResult && (
            <div style={{ marginTop: '1rem', border: '1px solid #e5e7eb', borderRadius: '6px', padding: '1rem', backgroundColor: '#f9fafb' }}>
              <h3 style={{ margin: '0 0 0.5rem 0', fontSize: '1.1rem' }}>{productResult.data.name}</h3>
              <p style={{ margin: '0 0 1rem 0', color: '#10b981', fontWeight: 'bold' }}>${productResult.data.price}</p>
              
              {/* Dynamic Badges based on backend data routing feedback */}
              <div style={{ display: 'flex', gap: '0.5rem', marginBottom: '1rem' }}>
                {productResult.dataSource === 'Redis Cache' ? (
                  <span style={{ display: 'inline-flex', alignItems: 'center', gap: '0.25rem', backgroundColor: '#dbeafe', color: '#1e40af', padding: '0.25rem 0.5rem', borderRadius: '9999px', fontSize: '0.75rem', fontWeight: 600 }}>
                    <Zap size={12} /> Cache Hit (Redis)
                  </span>
                ) : (
                  <span style={{ display: 'inline-flex', alignItems: 'center', gap: '0.25rem', backgroundColor: '#fef3c7', color: '#92400e', padding: '0.25rem 0.5rem', borderRadius: '9999px', fontSize: '0.75rem', fontWeight: 600 }}>
                    <Database size={12} /> Cache Miss (Postgres)
                  </span>
                )}
                
                <span style={{ backgroundColor: productResult.stockInfo.inStock ? '#d1fae5' : '#fee2e2', color: productResult.stockInfo.inStock ? '#065f46' : '#991b1b', padding: '0.25rem 0.5rem', borderRadius: '9999px', fontSize: '0.75rem', fontWeight: 600 }}>
                  gRPC Stock: {productResult.stockInfo.available} units
                </span>
              </div>
            </div>
          )}
        </section>

        {/* PANEL 2: ASYNC RABBITMQ WRITING PATHWAY */}
        <section style={{ backgroundColor: '#ffffff', borderRadius: '8px', padding: '1.5rem', boxShadow: '0 1px 3px rgba(0,0,0,0.1)' }}>
          <h2 style={{ fontSize: '1.25rem', marginTop: 0, display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
            <ShoppingCart size={20} color="#10b981" /> Asynchronous Events
          </h2>
          <p style={{ color: '#6b7280', fontSize: '0.875rem' }}>Publishes an instant event execution task to RabbitMQ. A Python worker background loop dequeues and completes processing separately.</p>

          <form onSubmit={handleCheckout} style={{ display: 'flex', flexDirection: 'column', gap: '1rem', marginTop: '1.5rem' }}>
            <div>
              <label style={{ display: 'block', fontSize: '0.875rem', fontWeight: 600, marginBottom: '0.25rem' }}>Quantity to Order</label>
              <input 
                type="number" 
                min="1" 
                value={checkoutQuantity} 
                onChange={(e) => setCheckoutQuantity(parseInt(e.target.value) || 1)} 
                style={{ padding: '0.5rem', border: '1px solid #d1d5db', borderRadius: '4px', width: '100%' }}
              />
            </div>
            
            <button 
              type="submit" 
              disabled={submittingOrder}
              style={{ backgroundColor: '#10b981', color: '#fff', border: 'none', padding: '0.75rem', borderRadius: '4px', cursor: 'pointer', fontWeight: 600, display: 'flex', justifyContent: 'center', alignItems: 'center', gap: '0.5rem' }}
            >
              {submittingOrder ? 'Publishing Task...' : 'Fire Async Checkout'}
            </button>
          </form>

          {orderMessage && (
            <div style={{ marginTop: '1.5rem', display: 'flex', gap: '0.5rem', backgroundColor: '#ecfdf5', border: '1px solid #a7f3d0', color: '#065f46', borderRadius: '6px', padding: '0.75rem', fontSize: '0.875rem' }}>
              <CheckCircle size={18} style={{ flexShrink: 0, marginTop: '0.1rem' }} />
              <span>{orderMessage}</span>
            </div>
          )}
        </section>

      </main>
    </div>
  );
}
